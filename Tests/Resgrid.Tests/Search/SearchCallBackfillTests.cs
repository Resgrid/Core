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
	/// The call history backfill (M0261) restores calls older rebuilds retired without changing the index generation: each
	/// sweep walks one batch per pending department, newest CallId first, projects only the calls search does not hold and
	/// saves its cursor; a full rebuild that projected every call leaves nothing to backfill.
	/// </summary>
	[TestFixture]
	public class SearchCallBackfillTests
	{
		private const int Dept = 5;
		private SearchIndexState _state;
		private List<Call> _calls;
		private HashSet<string> _present;
		private List<string> _projected;
		private List<(int? Cursor, DateTime? Completed)> _saved;
		private bool _flagOn;
		private Mock<ISearchProjectionsRepository> _projections;
		private Mock<ISearchIndexStatesRepository> _states;
		private Mock<ISearchProjectionService> _projectionService;
		private Mock<ICallsService> _callsService;
		private SearchIndexMaintenanceService _service;
		private int _batch;
		private int _seconds;
		private int _years;

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
			_batch = SearchConfig.CallBackfillBatchSize;
			_seconds = SearchConfig.CallBackfillSecondsPerSweep;
			_years = SearchConfig.CallRebuildYears;
			SearchConfig.CallBackfillBatchSize = 3;
			SearchConfig.CallBackfillSecondsPerSweep = 20;
			SearchConfig.CallRebuildYears = 0;

			_flagOn = true;
			_state = new SearchIndexState
			{
				SearchIndexStateId = 1, IndexName = SearchIndexNames.Global, DepartmentId = Dept, State = (int)SearchIndexBuildState.Ready,
				Generation = GlobalSearchGeneration.Compute(0, 0), SchemaVersion = GlobalSearchGeneration.SchemaVersion, ModifiedOn = DateTime.UtcNow.AddHours(-1)
			};
			// Calls 1..7; the recent ones (5..7) were projected by the old three-year rebuild.
			_calls = Enumerable.Range(1, 7).Select(i => new Call { CallId = i, DepartmentId = Dept, State = 1 }).ToList();
			_present = new HashSet<string> { "5", "6", "7" };
			_projected = new List<string>();
			_saved = new List<(int?, DateTime?)>();

			_states = new Mock<ISearchIndexStatesRepository>();
			_states.Setup(s => s.GetAllForIndexAsync(SearchIndexNames.Global)).ReturnsAsync(() => new List<SearchIndexState> { _state });
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, Dept)).ReturnsAsync(() => _state);
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((SearchIndexState s, CancellationToken _, bool __) => _saved.Add((s.CallBackfillCursor, s.CallBackfillCompletedOn)))
				.ReturnsAsync((SearchIndexState s, CancellationToken _, bool __) => s);

			_projections = new Mock<ISearchProjectionsRepository>();
			_projections.Setup(p => p.GetCallsForBackfillAsync(Dept, It.IsAny<int?>(), It.IsAny<int>()))
				.ReturnsAsync((int _, int? before, int take) => _calls.Where(c => !before.HasValue || c.CallId < before.Value).OrderByDescending(c => c.CallId).Take(take).ToList());
			_projections.Setup(p => p.GetByEntityIdsAsync(Dept, SearchEntityTypes.Call, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, string __, IEnumerable<string> ids) => ids.Where(_present.Contains)
					.Select(id => new SearchProjection { DepartmentId = Dept, EntityType = SearchEntityTypes.Call, EntityId = id }).ToList());
			_projections.Setup(p => p.GetLivePageAsync(Dept, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<SearchProjection>());

			_projectionService = new Mock<ISearchProjectionService>();
			_projectionService.Setup(p => p.BuildCallAsync(It.IsAny<Call>()))
				.ReturnsAsync((Call c) => new SearchProjection { DepartmentId = Dept, EntityType = SearchEntityTypes.Call, EntityId = c.CallId.ToString() });
			_projectionService.Setup(p => p.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>()))
				.Callback((SearchProjection p, CancellationToken _) => { _projected.Add(p.EntityId); _present.Add(p.EntityId); })
				.ReturnsAsync((SearchProjection p, CancellationToken _) => p);

			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.SearchUnified, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(() => _flagOn);

			_callsService = new Mock<ICallsService>();
			_service = new SearchIndexMaintenanceService(_states.Object, _projections.Object, Mock.Of<IGlobalSearchIndexer>(), Mock.Of<IDepartmentDataProtectionService>(),
				flags.Object, _projectionService.Object, _callsService.Object, Mock.Of<IUnitsService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IContactsService>(), Mock.Of<IMessageService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>());
		}

		[TearDown]
		public void TearDown()
		{
			SearchConfig.Enabled = false;
			SearchConfig.CallBackfillBatchSize = _batch;
			SearchConfig.CallBackfillSecondsPerSweep = _seconds;
			SearchConfig.CallRebuildYears = _years;
		}

		[Test]
		public async Task Each_sweep_walks_one_batch_newest_first_and_projects_only_calls_search_does_not_hold()
		{
			var first = await _service.SweepAsync();

			_projected.Should().BeEmpty("calls 7, 6 and 5 are already in search, and re-projecting them would drop their hits until re-indexed");
			_state.CallBackfillCursor.Should().Be(5);
			_state.CallBackfillCompletedOn.Should().BeNull("a full batch may have more calls behind it");
			first.CallsBackfilled.Should().Be(0);

			var second = await _service.SweepAsync();

			_projected.Should().Equal("4", "3", "2");
			_state.CallBackfillCursor.Should().Be(2);
			second.CallsBackfilled.Should().Be(3);
			_state.State.Should().Be((int)SearchIndexBuildState.Ready, "the backfill never takes the department's results offline");
			_state.Generation.Should().Be(GlobalSearchGeneration.Compute(0, 0));

			var third = await _service.SweepAsync();

			_projected.Should().Equal("4", "3", "2", "1");
			_state.CallBackfillCompletedOn.Should().NotBeNull("a short batch reached the oldest call");
			third.CallBackfillsCompleted.Should().Be(1);

			_projections.Invocations.Clear();
			await _service.SweepAsync();
			_projections.Verify(p => p.GetCallsForBackfillAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never, "a finished department is not walked again");
		}

		[Test]
		public async Task Calls_retired_by_an_old_rebuild_are_projected_again()
		{
			_present.Remove("6"); // soft-deleted by a three-year rebuild: GetByEntityIdsAsync returns live rows only

			await _service.SweepAsync();

			_projected.Should().Equal("6");
		}

		[TestCase("flag")]
		[TestCase("rebuilding")]
		[TestCase("requested")]
		[TestCase("capped")]
		[TestCase("off")]
		public async Task Departments_that_are_not_ready_or_capped_are_not_walked(string reason)
		{
			if (reason == "flag") _flagOn = false;
			if (reason == "rebuilding") _state.State = (int)SearchIndexBuildState.Rebuilding;
			if (reason == "requested") _state.RebuildRequestedOn = DateTime.UtcNow;
			if (reason == "capped") SearchConfig.CallRebuildYears = 3;
			if (reason == "off") SearchConfig.CallBackfillSecondsPerSweep = 0;
			_present.Clear();

			await _service.SweepAsync();

			_projections.Verify(p => p.GetCallsForBackfillAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never);
			_state.CallBackfillCursor.Should().BeNull();
		}

		[Test]
		public async Task A_rebuild_request_made_after_the_list_was_read_is_not_overwritten()
		{
			var listed = new SearchIndexState
			{
				SearchIndexStateId = 1, IndexName = SearchIndexNames.Global, DepartmentId = Dept, State = (int)SearchIndexBuildState.Ready,
				Generation = _state.Generation, ModifiedOn = _state.ModifiedOn
			};
			_states.Setup(s => s.GetAllForIndexAsync(SearchIndexNames.Global)).ReturnsAsync(new List<SearchIndexState> { listed });
			_state.RebuildRequestedOn = DateTime.UtcNow;

			await _service.SweepAsync();

			_projections.Verify(p => p.GetCallsForBackfillAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never);
			_saved.Should().NotContain(s => s.Cursor.HasValue);
		}

		[TestCase(true, true)]
		[TestCase(false, false)]
		public async Task A_full_rebuild_leaves_nothing_to_backfill_only_when_every_call_was_projected(bool callsSucceed, bool completed)
		{
			_state.CallBackfillCursor = 4;
			_callsService.Setup(c => c.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(new List<Call>());
			if (callsSucceed)
				_callsService.Setup(c => c.GetCallYearsByDeptartmentAsync(Dept)).ReturnsAsync(new List<string>());
			else
				_callsService.Setup(c => c.GetCallYearsByDeptartmentAsync(Dept)).ThrowsAsync(new TimeoutException("dev database"));

			await _service.RebuildDepartmentAsync(Dept);

			_state.CallBackfillCursor.Should().BeNull();
			(_state.CallBackfillCompletedOn != null).Should().Be(completed, "a failed call family keeps the old rows and the backfill covers the gap");
		}
	}
}
