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
		private List<RecordsSearchDocumentSource> _indexed;
		private List<RmsSearchIndexState> _savedStates;
		private RecordsSearchIndexMaintenanceService _service;

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
			_indexed = new List<RecordsSearchDocumentSource>();
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

			_adp = new Mock<IDepartmentDataProtectionService>();
			_adp.Setup(a => a.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(false);
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(3);
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, PolicyEpoch = 7 });

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsSearchConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsSearchConfig { IndexNarrative = true });

			_service = new RecordsSearchIndexMaintenanceService(_cutovers.Object, _projections.Object, _states.Object, _details.Object, _indexer.Object, _adp.Object, _settings.Object);
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
			_indexed.Should().OnlyContain(d => d.Generation == "1.3.7" && d.Narrative.StartsWith("narrative of "));

			_savedStates.Select(s => (RmsSearchIndexBuildState)s.State).Should().Equal(RmsSearchIndexBuildState.Rebuilding, RmsSearchIndexBuildState.Ready);
			var final = _savedStates.Last();
			final.Generation.Should().Be("1.3.7");
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
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = "1.3.7", State = (int)RmsSearchIndexBuildState.Ready, LastIndexedModifiedOn = since });
			var deleted = Projection("rec-9", new DateTime(2026, 9, 3));
			deleted.DeletedOn = DateTime.UtcNow;
			_projections.SetupSequence(p => p.GetModifiedSinceAsync(Dept, since, It.IsAny<int>())).ReturnsAsync(new[] { Projection("rec-5", new DateTime(2026, 9, 2)), deleted });
			_projections.Setup(p => p.GetModifiedSinceAsync(Dept, new DateTime(2026, 9, 3), It.IsAny<int>())).ReturnsAsync(new RmsRecordSearchProjection[0]);

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
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = "1.2.7", State = (int)RmsSearchIndexBuildState.Ready });
			_projections.Setup(p => p.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new RmsRecordSearchProjection[0]);

			(await _service.SweepAsync()).DepartmentsRebuilt.Should().Be(1, "the catalog version moved from 2 to 3");

			_savedStates.Clear();
			_states.Setup(s => s.GetAsync(Dept, RmsSearchIndexState.RecordsIndexName)).ReturnsAsync(new RmsSearchIndexState { DepartmentId = Dept, IndexName = RmsSearchIndexState.RecordsIndexName, Generation = "1.3.7", State = (int)RmsSearchIndexBuildState.Failed });
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

		private static RmsRecordSearchProjection Projection(string id, DateTime modifiedOn)
		{
			return new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = id, DepartmentId = Dept, SourceType = (int)RmsSearchSourceType.Record, SourceId = id,
				RecordNumber = "TRN-2026-0001", DefinitionKey = RmsDefinitionKeys.Training, State = (int)RmsRecordState.Finalized,
				RecordCreatedOn = modifiedOn, ModifiedOn = modifiedOn, DisplaySummary = "x"
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
