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
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>Worker 44's sweep: generation-keyed rebuilds, incremental catch-up, and the narrative degrade path.</summary>
	[TestFixture]
	public class RecordsSearchIndexMaintenanceServiceTests
	{
		private const int Dept = 3;
		private Mock<IRmsDepartmentCutoversRepository> _cutovers;
		private Mock<IRmsRecordSearchProjectionsRepository> _projections;
		private Mock<IRmsSearchIndexStatesRepository> _states;
		private Mock<IRmsOperationalRecordDetailsRepository> _details;
		private Mock<IRecordsSearchIndexer> _indexer;
		private Mock<IDepartmentDataProtectionService> _adp;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IRmsRetentionRepository> _retention;
		private List<RecordsSearchDocumentSource> _indexed;
		private Dictionary<string, long> _indexedVersions;
		private List<RmsSearchIndexState> _savedStates;
		private RecordsSearchIndexMaintenanceService _service;

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
			_indexed = new List<RecordsSearchDocumentSource>();
			_indexedVersions = new Dictionary<string, long>();
			_savedStates = new List<RmsSearchIndexState>();

			_cutovers = new Mock<IRmsDepartmentCutoversRepository>();
			_cutovers.Setup(c => c.GetActiveAsync()).ReturnsAsync(new[] { new RmsDepartmentCutover { DepartmentId = Dept, State = (int)RmsDepartmentCutoverState.Active } });

			_projections = new Mock<IRmsRecordSearchProjectionsRepository>();
			_states = new Mock<IRmsSearchIndexStatesRepository>();
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<RmsSearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSearchIndexState st, CancellationToken c, bool b) => { _savedStates.Add(Clone(st)); return st; });

			_details = new Mock<IRmsOperationalRecordDetailsRepository>();
			_details.Setup(d => d.GetDraftAsync(Dept, It.IsAny<string>())).ReturnsAsync((int dept, string id) => new RmsOperationalRecordDetail { RecordId = id, Narrative = "narrative of " + id });

			_indexer = new Mock<IRecordsSearchIndexer>();
			_indexer.Setup(i => i.IndexAsync(It.IsAny<IEnumerable<RecordsSearchDocumentSource>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IEnumerable<RecordsSearchDocumentSource> docs, CancellationToken c) => { var list = docs.ToList(); _indexed.AddRange(list); return list.Count; });
			_indexer.Setup(i => i.CountDocumentsAsync(Dept)).ReturnsAsync(() => _indexed.Count);
			_indexer.Setup(i => i.GetIndexedRowVersionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<RmsRecordSearchProjection>>()))
				.ReturnsAsync((int _, IEnumerable<RmsRecordSearchProjection> rows) => (IDictionary<string, long>)rows
					.Where(r => _indexedVersions.ContainsKey(r.RmsRecordSearchProjectionId)).ToDictionary(r => r.RmsRecordSearchProjectionId, r => _indexedVersions[r.RmsRecordSearchProjectionId]));

			_adp = new Mock<IDepartmentDataProtectionService>();
			_adp.Setup(a => a.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(false);
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(3);
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, PolicyEpoch = 7 });

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsSearchConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsSearchConfig { IndexNarrative = true });

			_retention = new Mock<IRmsRetentionRepository>();
			_service = new RecordsSearchIndexMaintenanceService(_cutovers.Object, _projections.Object, _states.Object, _details.Object, _indexer.Object, _adp.Object, _settings.Object, _retention.Object);
		}

		[TearDown]
		public void TearDown() => SearchConfig.Enabled = false;

		[Test]
		public async Task First_sweep_rebuilds_the_department_and_records_the_generation()
		{
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync((RmsSearchIndexState)null);
			var page = new[] { Projection("rec-1", new DateTime(2026, 9, 1)), Projection("rec-2", new DateTime(2026, 9, 2)) };
			_projections.SetupSequence(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(page).ReturnsAsync(new RmsRecordSearchProjection[0]);

			var result = await _service.SweepAsync();

			result.DepartmentsChecked.Should().Be(1);
			result.DepartmentsRebuilt.Should().Be(1);
			result.DocumentsIndexed.Should().Be(2);
			result.Errors.Should().Be(0);
			_indexer.Verify(i => i.DeleteDepartmentAsync(Dept, It.IsAny<CancellationToken>()), Times.Once);
			_indexer.Verify(i => i.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
			_indexed.Should().OnlyContain(d => d.Generation == RecordsSearchGeneration.Compute(3, 7) && d.Narrative.StartsWith("narrative of "));

			_savedStates.Select(s => (RmsSearchIndexBuildState)s.State).Should().Equal(RmsSearchIndexBuildState.Rebuilding, RmsSearchIndexBuildState.Ready);
			var final = _savedStates.Last();
			final.Generation.Should().Be(RecordsSearchGeneration.Compute(3, 7));
			final.SchemaVersion.Should().Be(RecordsSearchGeneration.SchemaVersion);
			final.ProtectedCatalogVersion.Should().Be(3);
			final.PolicyEpoch.Should().Be(7);
			final.DocumentCount.Should().Be(2);
			final.LastIndexedModifiedOn.Should().Be(new DateTime(2026, 9, 2));
		}

		[Test]
		public async Task Matching_generation_catches_up_modified_rows_and_removes_soft_deleted_ones()
		{
			var since = new DateTime(2026, 9, 1);
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(3, 7), State = (int)RmsSearchIndexBuildState.Ready, LastIndexedModifiedOn = since });
			var deleted = Projection("rec-9", new DateTime(2026, 9, 3));
			deleted.DeletedOn = DateTime.UtcNow;
			_projections.SetupSequence(p => p.GetModifiedSinceAsync(Dept, since.AddSeconds(-1), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new[] { Projection("rec-5", new DateTime(2026, 9, 2)), deleted });
			_projections.Setup(p => p.GetModifiedSinceAsync(Dept, new DateTime(2026, 9, 3), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new RmsRecordSearchProjection[0]);

			var result = await _service.SweepAsync();

			result.DepartmentsRebuilt.Should().Be(0);
			result.DocumentsIndexed.Should().Be(1);
			result.DocumentsDeleted.Should().Be(1);
			_indexer.Verify(i => i.DeleteDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_indexer.Verify(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, "rec-9", It.IsAny<CancellationToken>()), Times.Once);
			_indexed.Select(d => d.Projection.SourceId).Should().Equal("rec-5");
			_savedStates.Last().LastIndexedModifiedOn.Should().Be(new DateTime(2026, 9, 3));
		}

		[Test]
		public async Task A_changed_generation_or_a_failed_state_forces_a_rebuild()
		{
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(2, 7), State = (int)RmsSearchIndexBuildState.Ready });
			_projections.Setup(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new RmsRecordSearchProjection[0]);

			(await _service.SweepAsync()).DepartmentsRebuilt.Should().Be(1, "the catalog version moved from 2 to 3");

			_savedStates.Clear();
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(3, 7), State = (int)RmsSearchIndexBuildState.Failed });
			(await _service.SweepAsync()).DepartmentsRebuilt.Should().Be(1, "a failed build is retried");
		}

		[Test]
		public async Task Narrative_is_withheld_for_protected_departments_and_when_the_department_opted_out()
		{
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync((RmsSearchIndexState)null);
			_projections.SetupSequence(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new[] { Projection("rec-1", new DateTime(2026, 9, 1)) }).ReturnsAsync(new RmsRecordSearchProjection[0]);
			_adp.Setup(a => a.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(true);

			await _service.SweepAsync();
			_indexed.Should().OnlyContain(d => d.Narrative == null, "enrollment withdraws narrative search");
			_details.Verify(d => d.GetDraftAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);

			_indexed.Clear();
			_adp.Setup(a => a.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(false);
			_settings.Setup(s => s.GetRecordsSearchConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsSearchConfig { IndexNarrative = false });
			_projections.SetupSequence(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new[] { Projection("rec-1", new DateTime(2026, 9, 1)) }).ReturnsAsync(new RmsRecordSearchProjection[0]);
			await _service.RebuildDepartmentAsync(Dept);
			_indexed.Should().OnlyContain(d => d.Narrative == null);
		}

		[Test]
		public async Task Disabled_host_skips_and_a_department_fault_is_counted_not_fatal()
		{
			SearchConfig.Enabled = false;
			var skipped = await _service.SweepAsync();
			skipped.Skipped.Should().BeTrue();
			_cutovers.Verify(c => c.GetActiveAsync(), Times.Never);

			SearchConfig.Enabled = true;
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ThrowsAsync(new InvalidOperationException("db down"));
			var result = await _service.SweepAsync();
			result.Errors.Should().Be(1);
			result.DepartmentsChecked.Should().Be(1);
		}

		[Test]
		public async Task Equal_timestamp_pages_include_a_late_purge_tombstone_at_the_checkpoint_boundary()
		{
			var previousBatch = SearchConfig.IndexBatchSize;
			try
			{
				SearchConfig.IndexBatchSize = 50;
				var stamp = new DateTime(2026, 9, 2);
				_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState
				{ DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(3, 7), State = (int)RmsSearchIndexBuildState.Ready, LastIndexedModifiedOn = stamp });
				var rows = Enumerable.Range(0, 101).Select(i => Projection("rec-" + i.ToString("D3"), stamp)).ToList();
				rows.Last().DeletedOn = stamp;
				// The late tombstone's document is still in the index; the live rows at the boundary were never indexed.
				_indexedVersions["rec-100"] = 0;
				_projections.Setup(p => p.GetModifiedSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync((int department, DateTime? since, int take, string id) => rows.Where(row => !since.HasValue || row.ModifiedOn > since.Value || row.ModifiedOn == since.Value && id != null && string.CompareOrdinal(row.RmsRecordSearchProjectionId, id) > 0).Take(take).ToList());
				var result = await _service.SweepAsync();
				result.Errors.Should().Be(0);
				_indexed.Select(d => d.Projection.SourceId).Should().HaveCount(100).And.OnlyHaveUniqueItems();
				_indexer.Verify(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, "rec-100", It.IsAny<CancellationToken>()), Times.Once);
				_projections.Verify(p => p.GetModifiedSinceAsync(Dept, stamp, 50, "rec-049"), Times.Once);
				_projections.Verify(p => p.GetModifiedSinceAsync(Dept, stamp, 50, "rec-099"), Times.Once);
				_savedStates.Single().LastIndexedModifiedOn.Should().Be(stamp);
			}
			finally { SearchConfig.IndexBatchSize = previousBatch; }
		}

		[Test]
		public async Task Failed_index_commit_does_not_advance_database_checkpoint_and_next_sweep_replays_the_tombstone()
		{
			var stamp = new DateTime(2026, 9, 2);
			var state = new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(3, 7), State = (int)RmsSearchIndexBuildState.Ready, LastIndexedModifiedOn = stamp };
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(() => Clone(state));
			var deleted = Projection("purged", stamp.AddMinutes(1)); deleted.DeletedOn = deleted.ModifiedOn;
			_projections.Setup(p => p.GetModifiedSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new[] { deleted });
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("disk unavailable"));
			(await _service.SweepAsync()).Errors.Should().Be(1);
			_savedStates.Should().BeEmpty();
			state.LastIndexedModifiedOn.Should().Be(stamp);
			var committed = false;
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => committed = true).Returns(Task.CompletedTask);
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<RmsSearchIndexState>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((RmsSearchIndexState checkpoint, CancellationToken ct, bool save) => { committed.Should().BeTrue(); _savedStates.Add(Clone(checkpoint)); return checkpoint; });
			(await _service.SweepAsync()).Errors.Should().Be(0);
			_indexer.Verify(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, "purged", It.IsAny<CancellationToken>()), Times.Exactly(2));
			_savedStates.Single().LastIndexedModifiedOn.Should().Be(deleted.ModifiedOn);
		}

		[Test]
		public async Task A_quiet_sweep_commits_nothing_when_the_overlap_window_only_re_reads_indexed_rows()
		{
			// The read re-covers the second before the checkpoint; this row is already indexed at the same version. Before the
			// overlap was filtered every quiet sweep re-indexed it and committed and published once per department (QA:
			// RESGRID-WEBJOBS, ~15 publishes a minute and one Sentry event per department while the object store timed out).
			var stamp = new DateTime(2026, 9, 2);
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(ReadyState(Dept, stamp));
			var reRead = Projection("rec-1", stamp, rowVersion: 4);
			_indexedVersions[reRead.RmsRecordSearchProjectionId] = 4;
			_projections.Setup(p => p.GetModifiedSinceAsync(Dept, stamp.AddSeconds(-1), It.IsAny<int>(), null)).ReturnsAsync(new[] { reRead });

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(0);
			result.DocumentsIndexed.Should().Be(0);
			_indexer.Verify(i => i.IndexAsync(It.IsAny<IEnumerable<RecordsSearchDocumentSource>>(), It.IsAny<CancellationToken>()), Times.Never);
			_indexer.Verify(i => i.CommitAsync(It.IsAny<CancellationToken>()), Times.Never, "nothing changed: no commit, so no publish");
			_savedStates.Should().BeEmpty();
		}

		[Test]
		public async Task An_overlap_row_the_index_lacks_or_holds_at_an_older_version_is_still_indexed()
		{
			var stamp = new DateTime(2026, 9, 2);
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(ReadyState(Dept, stamp));
			var late = Projection("rec-1", stamp.AddMilliseconds(-300), rowVersion: 1);
			var updated = Projection("rec-2", stamp, rowVersion: 3);
			var unchanged = Projection("rec-3", stamp, rowVersion: 5);
			_indexedVersions[updated.RmsRecordSearchProjectionId] = 2;
			_indexedVersions[unchanged.RmsRecordSearchProjectionId] = 5;
			var goneAlready = Projection("rec-4", stamp, rowVersion: 2);
			goneAlready.DeletedOn = stamp;
			_projections.Setup(p => p.GetModifiedSinceAsync(Dept, stamp.AddSeconds(-1), It.IsAny<int>(), null)).ReturnsAsync(new[] { late, updated, unchanged, goneAlready });

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(0);
			_indexed.Select(d => d.Projection.SourceId).Should().Equal("rec-1", "rec-2");
			_indexer.Verify(i => i.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "the index no longer holds the deleted row");
			_indexer.Verify(i => i.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
			_savedStates.Single().LastIndexedModifiedOn.Should().Be(stamp);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Catch_ups_share_one_commit_per_sweep_and_no_checkpoint_is_saved_before_it(bool failCommit)
		{
			const int other = 4;
			var stamp = new DateTime(2026, 9, 2);
			_cutovers.Setup(c => c.GetActiveAsync()).ReturnsAsync(new[]
			{
				new RmsDepartmentCutover { DepartmentId = Dept, State = (int)RmsDepartmentCutoverState.Active },
				new RmsDepartmentCutover { DepartmentId = other, State = (int)RmsDepartmentCutoverState.Active }
			});
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(other)).ReturnsAsync(3);
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(other, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = other, PolicyEpoch = 7 });
			foreach (var department in new[] { Dept, other })
			{
				_states.Setup(s => s.GetAsync(department, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(ReadyState(department, stamp));
				_projections.Setup(p => p.GetModifiedSinceAsync(department, stamp.AddSeconds(-1), It.IsAny<int>(), null))
					.ReturnsAsync(new[] { Projection("rec-" + department, stamp.AddMinutes(department), departmentId: department) });
			}

			var commits = 0;
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).Returns(() =>
			{
				commits++;
				return failCommit ? Task.FromException(new InvalidOperationException("Io error: timeout")) : Task.CompletedTask;
			});
			_states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<RmsSearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSearchIndexState st, CancellationToken c, bool b) => { commits.Should().Be(1, "a checkpoint must never lead the committed segments"); _savedStates.Add(Clone(st)); return st; });

			var result = await _service.SweepAsync();

			commits.Should().Be(1, "one commit-and-publish per sweep, not one per department");
			result.DocumentsIndexed.Should().Be(2);
			if (failCommit)
			{
				result.Errors.Should().Be(1, "an object store outage is one failure per sweep, not one per department");
				_savedStates.Should().BeEmpty();
			}
			else
			{
				result.Errors.Should().Be(0);
				_savedStates.Select(s => (s.DepartmentId, s.LastIndexedModifiedOn)).Should().Equal((Dept, stamp.AddMinutes(Dept)), (other, stamp.AddMinutes(other)));
			}
		}

		[Test]
		public async Task A_rebuild_cannot_publish_ready_before_the_index_commit_succeeds()
		{
			_projections.Setup(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new RmsRecordSearchProjection[0]);
			_indexer.Setup(i => i.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("disk unavailable"));
			(await _service.SweepAsync()).Errors.Should().Be(1);
			_savedStates.Select(state => (RmsSearchIndexBuildState)state.State).Should().Equal(RmsSearchIndexBuildState.Rebuilding, RmsSearchIndexBuildState.Failed);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Durable_erasure_runs_with_indexing_disabled_and_is_acknowledged_only_after_expunge(bool failCommit)
		{
			SearchConfig.Enabled = false;
			var target = new RmsSearchErasureTarget { DepartmentId = Dept, RecordId = "purged", RecordKind = (int)RmsRecordKind.IncidentReport, PurgedOn = DateTime.UtcNow, SourceIds = new List<string> { "purged", "analysis" } };
			_retention.Setup(r => r.GetPendingSearchErasuresAsync(100, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<RmsSearchErasureTarget> { target });
			var expunged = false;
			_indexer.Setup(i => i.ExpungeDeletesAsync(It.IsAny<CancellationToken>())).Returns(() =>
			{
				if (failCommit) throw new InvalidOperationException("index commit failed");
				expunged = true; return Task.CompletedTask;
			});
			_retention.Setup(r => r.CompleteSearchErasureAsync(target, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => { expunged.Should().BeTrue(); return true; });
			var result = await _service.SweepAsync();
			result.Skipped.Should().BeTrue();
			result.SearchErasuresCompleted.Should().Be(failCommit ? 0 : 1);
			result.Errors.Should().Be(failCommit ? 1 : 0);
			_indexer.Verify(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, "purged", It.IsAny<CancellationToken>()), Times.Once);
			_indexer.Verify(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, "analysis", It.IsAny<CancellationToken>()), Times.Once);
			_retention.Verify(r => r.CompleteSearchErasureAsync(target, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), failCommit ? Times.Never() : Times.Once());
			_cutovers.Verify(c => c.GetActiveAsync(), Times.Never);
		}

		[Test]
		public async Task A_failed_erasure_page_does_not_starve_later_pending_reports()
		{
			SearchConfig.Enabled = false;
			var first = Enumerable.Range(0, 100).Select(i => new RmsSearchErasureTarget { DepartmentId = Dept, RecordId = "purged-" + i.ToString("D3"), PurgedOn = DateTime.UtcNow }).ToList();
			var last = new RmsSearchErasureTarget { DepartmentId = Dept, RecordId = "purged-100", PurgedOn = DateTime.UtcNow };
			_retention.Setup(r => r.GetPendingSearchErasuresAsync(100, null, It.IsAny<CancellationToken>())).ReturnsAsync(first);
			_retention.Setup(r => r.GetPendingSearchErasuresAsync(100, first.Last(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<RmsSearchErasureTarget> { last });
			_indexer.Setup(i => i.DeleteAsync(Dept, (int)RmsSearchSourceType.Record, It.Is<string>(id => id != last.RecordId), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("source unavailable"));
			_retention.Setup(r => r.CompleteSearchErasureAsync(last, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var result = await _service.SweepAsync();
			result.Errors.Should().Be(100); result.SearchErasuresCompleted.Should().Be(1);
			_indexer.Verify(i => i.ExpungeDeletesAsync(It.IsAny<CancellationToken>()), Times.Once);
			_retention.Verify(r => r.CompleteSearchErasureAsync(It.IsAny<RmsSearchErasureTarget>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task A_deleted_departments_index_slice_is_erased_before_its_marker_is_dropped(bool failCommit)
		{
			SearchConfig.Enabled = false;
			const int deletedDepartment = 13275;
			var order = new List<string>();
			_states.Setup(s => s.GetDeletedDepartmentIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { deletedDepartment });
			_indexer.Setup(i => i.DeleteDepartmentAsync(deletedDepartment, It.IsAny<CancellationToken>())).Callback(() => order.Add("delete")).Returns(Task.CompletedTask);
			_indexer.Setup(i => i.ExpungeDeletesAsync(It.IsAny<CancellationToken>())).Returns(() =>
			{
				if (failCommit) throw new InvalidOperationException("index commit failed");
				order.Add("expunge"); return Task.CompletedTask;
			});
			_states.Setup(s => s.DeleteForDeletedDepartmentAsync(deletedDepartment, It.IsAny<CancellationToken>())).Callback(() => order.Add("marker")).ReturnsAsync(1);

			var result = await _service.SweepAsync();

			result.Skipped.Should().BeTrue("deleted-department erasure runs even while search indexing is disabled");
			result.DeletedDepartmentsErased.Should().Be(failCommit ? 0 : 1);
			result.Errors.Should().Be(failCommit ? 1 : 0);
			order.Should().Equal(failCommit ? new[] { "delete" } : new[] { "delete", "expunge", "marker" }, "the marker must outlive any uncommitted erasure");
			_indexer.Verify(i => i.IndexAsync(It.IsAny<IEnumerable<RecordsSearchDocumentSource>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_sweep_that_overlaps_a_running_one_skips_instead_of_sharing_the_writer()
		{
			var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_cutovers.Setup(c => c.GetActiveAsync()).Returns(async () =>
			{
				entered.TrySetResult(true);
				await release.Task;
				return (IEnumerable<RmsDepartmentCutover>)new List<RmsDepartmentCutover>();
			});

			var first = _service.SweepAsync();
			await entered.Task;
			var second = await _service.SweepAsync();
			release.SetResult(true);
			var firstResult = await first;

			second.Skipped.Should().BeTrue();
			second.Errors.Should().Be(0, "a skipped sweep is not a failed one, so the scheduler does not retry it");
			_retention.Verify(r => r.GetPendingSearchErasuresAsync(It.IsAny<int>(), It.IsAny<RmsSearchErasureTarget>(), It.IsAny<CancellationToken>()), Times.Once, "the skipped sweep must not touch the writer, erasures included");
			firstResult.Skipped.Should().BeFalse();
			(await _service.SweepAsync()).Skipped.Should().BeFalse("the gate is released when the running sweep ends");
		}

		private static RmsRecordSearchProjection Projection(string id, DateTime modifiedOn, long rowVersion = 0, int departmentId = Dept)
		{
			return new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = id, DepartmentId = departmentId, SourceType = (int)RmsSearchSourceType.Record, SourceId = id,
				RecordNumber = "TRN-2026-0001", DefinitionKey = RmsDefinitionKeys.Training, State = (int)RmsRecordState.Finalized,
				RecordCreatedOn = modifiedOn, ModifiedOn = modifiedOn, DisplaySummary = "x", RowVersion = rowVersion
			};
		}

		private static RmsSearchIndexState ReadyState(int departmentId, DateTime checkpoint)
		{
			return new RmsSearchIndexState
			{
				DepartmentId = departmentId, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = RecordsSearchGeneration.Compute(3, 7),
				State = (int)RmsSearchIndexBuildState.Ready, LastIndexedModifiedOn = checkpoint
			};
		}

		private static RmsSearchIndexState Clone(RmsSearchIndexState s)
		{
			return new RmsSearchIndexState
			{
				RmsSearchIndexStateId = s.RmsSearchIndexStateId, DepartmentId = s.DepartmentId, IndexName = s.IndexName, SchemaVersion = s.SchemaVersion,
				ProtectedCatalogVersion = s.ProtectedCatalogVersion, PolicyEpoch = s.PolicyEpoch, Generation = s.Generation, State = s.State,
				DocumentCount = s.DocumentCount, LastRebuiltOn = s.LastRebuiltOn, LastIndexedModifiedOn = s.LastIndexedModifiedOn
			};
		}
	}
}
