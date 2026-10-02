using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Worker 70's catch-up sweep: one commit for the whole sweep with checkpoints saved only after it, rows its overlap
	/// window re-reads unchanged are skipped (a quiet sweep commits nothing), and a sweep that overlaps a running one skips.
	/// </summary>
	[TestFixture]
	public class SearchIndexSweepTests
	{
		private static readonly DateTime Checkpoint = new DateTime(2026, 10, 2, 7, 0, 0, DateTimeKind.Utc);
		private Mock<ISearchIndexStatesRepository> _states;
		private Mock<ISearchProjectionsRepository> _projections;
		private Mock<IGlobalSearchIndexer> _indexer;
		private Mock<IFeatureToggleService> _flags;
		private List<SearchIndexState> _rows;
		private Dictionary<int, List<SearchProjection>> _changes;
		private Dictionary<string, long> _indexed;
		private List<string> _calls;
		private SearchIndexMaintenanceService _service;

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
			_calls = new List<string>();
			_rows = new List<SearchIndexState> { State(1), State(2) };
			_changes = new Dictionary<int, List<SearchProjection>> { [1] = new List<SearchProjection>(), [2] = new List<SearchProjection>() };
			_indexed = new Dictionary<string, long>();

			_states = new Mock<ISearchIndexStatesRepository>();
			_states.Setup(s => s.GetAllForIndexAsync(SearchIndexNames.Global)).ReturnsAsync(() => _rows);
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((SearchIndexState s, CancellationToken _, bool __) => _calls.Add("save:" + s.DepartmentId))
				.ReturnsAsync((SearchIndexState s, CancellationToken _, bool __) => s);

			_projections = new Mock<ISearchProjectionsRepository>();
			_projections.Setup(p => p.GetModifiedSinceAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int department, DateTime? since, int take, string sinceId) => sinceId == null
					? _changes[department].Where(r => !since.HasValue || r.ModifiedOn > since.Value).ToList()
					: new List<SearchProjection>());

			_indexer = new Mock<IGlobalSearchIndexer>();
			_indexer.Setup(i => i.IndexAsync(It.IsAny<IEnumerable<SearchProjection>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((IEnumerable<SearchProjection> rows, string _, CancellationToken __) => _calls.Add("index:" + string.Join(",", rows.Select(r => r.EntityId))))
				.ReturnsAsync((IEnumerable<SearchProjection> rows, string _, CancellationToken __) => rows.Count());
			_indexer.Setup(i => i.DeleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int _, string __, string id, CancellationToken ___) => _calls.Add("delete:" + id)).Returns(Task.CompletedTask);
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("commit")).Returns(Task.CompletedTask);
			_indexer.Setup(i => i.CountDocumentsAsync(It.IsAny<int>())).ReturnsAsync(10);
			_indexer.Setup(i => i.GetIndexedRowVersionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<SearchProjection>>()))
				.ReturnsAsync((int _, IEnumerable<SearchProjection> rows) => (IDictionary<string, long>)rows
					.Where(r => _indexed.ContainsKey(r.SearchProjectionId)).ToDictionary(r => r.SearchProjectionId, r => _indexed[r.SearchProjectionId]));

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.SearchUnified, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_service = new SearchIndexMaintenanceService(_states.Object, _projections.Object, _indexer.Object, Mock.Of<IDepartmentDataProtectionService>(),
				_flags.Object, Mock.Of<ISearchProjectionService>(), Mock.Of<ICallsService>(), Mock.Of<IUnitsService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IContactsService>(), Mock.Of<IMessageService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>());
		}

		[TearDown]
		public void TearDown() => SearchConfig.Enabled = false;

		private static SearchIndexState State(int department) => new SearchIndexState
		{
			IndexName = SearchIndexNames.Global,
			DepartmentId = department,
			State = (int)SearchIndexBuildState.Ready,
			Generation = GlobalSearchGeneration.Compute(0, 0),
			DocumentCount = 10,
			LastIndexedModifiedOn = Checkpoint
		};

		private static SearchProjection Row(int department, string id, DateTime modified, long version = 1, bool deleted = false) => new SearchProjection
		{
			SearchProjectionId = "p-" + id,
			DepartmentId = department,
			EntityType = SearchEntityTypes.Call,
			EntityId = id,
			RowVersion = version,
			ModifiedOn = modified,
			DeletedOn = deleted ? modified : (DateTime?)null
		};

		[Test]
		public async Task Changes_in_several_departments_commit_once_and_checkpoints_follow_the_commit()
		{
			_changes[1].Add(Row(1, "10", Checkpoint.AddMinutes(1)));
			_changes[2].Add(Row(2, "20", Checkpoint.AddMinutes(2)));

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(0);
			_calls.Count(c => c == "commit").Should().Be(1, "one publish per sweep, not one per department");
			_calls.IndexOf("commit").Should().BeLessThan(_calls.IndexOf("save:1"));
			_calls.IndexOf("commit").Should().BeLessThan(_calls.IndexOf("save:2"));
			_rows[0].LastIndexedModifiedOn.Should().Be(Checkpoint.AddMinutes(1));
			_rows[1].LastIndexedModifiedOn.Should().Be(Checkpoint.AddMinutes(2));
			result.DocumentsIndexed.Should().Be(2);
		}

		[Test]
		public async Task A_quiet_sweep_commits_nothing_when_the_overlap_window_only_re_reads_indexed_rows()
		{
			// The read re-covers the second before the checkpoint; this row is already indexed at the same version.
			var reRead = Row(1, "10", Checkpoint.AddMilliseconds(-200), version: 4);
			_changes[1].Add(reRead);
			_indexed[reRead.SearchProjectionId] = 4;

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(0);
			_calls.Should().BeEmpty("nothing changed: no index write, no commit, no publish, no state write");
		}

		[Test]
		public async Task An_overlap_row_the_index_lacks_or_holds_at_an_older_version_is_still_indexed()
		{
			var late = Row(1, "10", Checkpoint.AddMilliseconds(-300), version: 1);
			var updated = Row(1, "11", Checkpoint, version: 3);
			_changes[1].AddRange(new[] { late, updated });
			_indexed[updated.SearchProjectionId] = 2;

			await _service.SweepAsync();

			_calls.Should().Contain("index:10,11");
			_calls.Should().Contain("commit");
			_calls.Should().Contain("save:1");
		}

		[Test]
		public async Task A_deleted_overlap_row_already_gone_from_the_index_is_not_deleted_again()
		{
			_changes[1].Add(Row(1, "10", Checkpoint.AddMilliseconds(-100), deleted: true));

			await _service.SweepAsync();

			_calls.Should().BeEmpty();
		}

		[Test]
		public async Task A_failed_commit_advances_no_checkpoint()
		{
			_changes[1].Add(Row(1, "10", Checkpoint.AddMinutes(1)));
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("publish lease held"));

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(1);
			_calls.Should().NotContain(c => c.StartsWith("save:"));
			_rows[0].LastIndexedModifiedOn.Should().Be(Checkpoint, "the next sweep re-reads from the stored checkpoint");
		}

		[Test]
		public async Task A_sweep_that_overlaps_a_running_one_skips_instead_of_sharing_the_writer()
		{
			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_states.Setup(s => s.GetAllForIndexAsync(SearchIndexNames.Global)).Returns(async () =>
			{
				entered.TrySetResult(true);
				await release.Task;
				return (IEnumerable<SearchIndexState>)new List<SearchIndexState>();
			});

			var first = _service.SweepAsync();
			await entered.Task;
			var second = await _service.SweepAsync();
			release.SetResult(true);
			var firstResult = await first;

			second.Skipped.Should().BeTrue();
			firstResult.Skipped.Should().BeFalse();
			(await _service.SweepAsync()).Skipped.Should().BeFalse("the gate is released when the running sweep ends");
		}
	}
}
