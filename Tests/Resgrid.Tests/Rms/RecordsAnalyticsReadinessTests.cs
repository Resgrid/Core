using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// RMS-6 readiness dashboard: the checklists, maintenance and inventory modules answer through their own seams and
	/// authorize the viewer themselves; RMS joins their answers on the unit id, adds the readiness-packet evidence it
	/// captured, and never counts what a module refused.
	/// </summary>
	[TestFixture]
	public class RecordsAnalyticsReadinessTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime Start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime End = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

		private RmsPreventionHarness _h;
		private Mock<IChecklistsService> _checklists;
		private Mock<IReadinessAccessService> _access;
		private Mock<IWorkOrderReportingService> _workOrders;
		private Mock<IInventoryCatalogService> _inventory;
		private Mock<IInventoryAuthorizationService> _inventoryAuth;
		private Mock<IRmsEvidenceArtifactsRepository> _evidence;
		private Mock<IRmsRecordParticipantsRepository> _participants;
		private Mock<IUnitsService> _unitsService;
		private List<RmsOperationalRecord> _records;
		private List<RmsRecordUnitResponse> _units;
		private List<RmsEvidenceArtifactHeader> _headers;
		private List<InventoryAsset> _assets;
		private List<InventoryLocation> _locations;
		private List<InventoryIssuance> _issuances;
		private List<WorkOrderReportEntry> _orders;
		private ChecklistComplianceSummary _summary;
		private RecordsAnalyticsService _svc;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			_checklists = new Mock<IChecklistsService>(); _access = new Mock<IReadinessAccessService>(); _workOrders = new Mock<IWorkOrderReportingService>();
			_inventory = new Mock<IInventoryCatalogService>(); _inventoryAuth = new Mock<IInventoryAuthorizationService>(); _evidence = new Mock<IRmsEvidenceArtifactsRepository>();
			_participants = new Mock<IRmsRecordParticipantsRepository>(); _unitsService = new Mock<IUnitsService>();
			_records = new List<RmsOperationalRecord>(); _units = new List<RmsRecordUnitResponse>(); _headers = new List<RmsEvidenceArtifactHeader>();
			_assets = new List<InventoryAsset>(); _locations = new List<InventoryLocation>(); _issuances = new List<InventoryIssuance>(); _orders = new List<WorkOrderReportEntry>();
			_summary = new ChecklistComplianceSummary();

			_h.Records.Setup(r => r.GetFinalizedInRangeAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync((int d, IEnumerable<int> s, DateTime a, DateTime b, int t) => _records.ToList());
			_h.Units.Setup(u => u.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _units.Where(u => set.Contains(u.RevisionId)).ToList(); });
			_participants.Setup(p => p.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordParticipant>());
			_h.Reports.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsIncidentReportQuery>())).ReturnsAsync(new List<RmsIncidentReport>());
			_evidence.Setup(e => e.GetHeadersByKindInRangeAsync(Dept, RmsEvidenceKind.ReadinessPacket, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(() => _headers.ToList());
			_unitsService.Setup(u => u.GetUnitsForDepartmentAsync(Dept)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 1, Name = "Engine 1", StationGroupId = 10 }, new Unit { UnitId = 2, Name = "Medic 2", StationGroupId = 20 }, new Unit { UnitId = 3, Name = "Brush 3" } });
			_unitsService.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<UnitState> { new UnitState { UnitId = 1, State = (int)UnitStateTypes.Available, Timestamp = T0 }, new UnitState { UnitId = 2, State = (int)UnitStateTypes.OutOfService, Timestamp = T0.AddDays(1) } });
			_access.Setup(a => a.CanUseChecklistsAsync(Dept)).ReturnsAsync(true);
			_inventoryAuth.Setup(a => a.IsEnabledAsync(Dept)).ReturnsAsync(true);
			_checklists.Setup(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>())).ReturnsAsync((ChecklistActor a, ChecklistReportQuery q) => { _summary.FromUtc = q.FromUtc; _summary.UntilUtc = q.UntilUtc; return _summary; });
			_workOrders.Setup(w => w.GetWorkOrderStatsAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderReportQuery>())).ReturnsAsync((ChecklistActor a, WorkOrderReportQuery q) => Stats(q));
			_workOrders.Setup(w => w.GetWorkOrderHistoryAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderReportQuery>())).ReturnsAsync((ChecklistActor a, WorkOrderReportQuery q) =>
			{
				var rows = _orders.Where(o => o.Order.Id > q.AfterId).OrderBy(o => o.Order.Id).ToList();
				return new WorkOrderHistoryPage { Items = rows.Take(50).ToList(), NextAfterId = rows.Count > 50 ? rows[49].Order.Id : (int?)null };
			});
			_inventory.Setup(i => i.ListAsync<InventoryAsset>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync((InventoryActor a, int page) => Page(_assets, page));
			_inventory.Setup(i => i.ListAsync<InventoryLocation>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync((InventoryActor a, int page) => Page(_locations, page));
			_inventory.Setup(i => i.ListAsync<InventoryIssuance>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync((InventoryActor a, int page) => Page(_issuances, page));
			var groups = new Mock<IDepartmentGroupsService>(); groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 10, Name = "Station 10" }, new DepartmentGroup { DepartmentGroupId = 20, Name = "Station 20" } });
			var departments = new Mock<IDepartmentsService>(); departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "UTC" });

			_svc = new RecordsAnalyticsService(_h.Gate, _h.Authorization.Object, _h.Records.Object, _h.Units.Object, _participants.Object, Mock.Of<IRmsRecordGroupScopesRepository>(), Mock.Of<IRmsOperationalRecordDetailsRepository>(), _h.Reports.Object,
				Mock.Of<IRmsUnitResponsesRepository>(), Mock.Of<IRmsIncidentTypesRepository>(), Mock.Of<IRmsRevisionsRepository>(), Mock.Of<IRmsRecordDueStatesRepository>(), _h.Inspections, _h.Programs, _h.Violations, _h.Permits, _h.PermitTypes, _h.Hydrants, _h.FlowTests, _h.Occupancies, _h.Crr,
				_unitsService.Object, groups.Object, departments.Object,
				_h.Flags.Object, _checklists.Object, _access.Object, _workOrders.Object, _inventory.Object, _inventoryAuth.Object, _evidence.Object);
		}

		private static InventoryPage<T> Page<T>(List<T> rows, int page) where T : InventoryRow => new InventoryPage<T> { Items = rows.Skip(page * 500).Take(500).ToList(), HasMore = rows.Count > (page + 1) * 500 };

		private WorkOrderStats Stats(WorkOrderReportQuery q)
		{
			var open = _orders.Where(o => (int)o.Order.Status <= 5).ToList();
			var stats = new WorkOrderStats { FromUtc = q.FromUtc ?? Start, UntilUtc = q.UntilUtc ?? End, AsOfUtc = DateTime.UtcNow, Total = _orders.Count, Open = open.Count, Overdue = open.Count(o => (int)o.Order.Status < 5 && o.Order.DueOn < DateTime.UtcNow), MeanTimeToRepairHours = 6.4m, RepairSamples = 3, MissingRepairTimes = 1 };
			foreach (var o in open) stats.OpenByPriority[(int)o.Order.Priority]++;
			stats.OpenByAge[0] = open.Count;
			stats.Costs.Add(new WorkOrderCostTotal { Currency = "USD", Labor = 120.5m, Parts = 80m, UnknownParts = 1 });
			return stats;
		}

		private static RecordsAnalyticsQuery Q() => new RecordsAnalyticsQuery { Start = Start, End = End };

		private static ChecklistComplianceGroup Group(ChecklistTargetType type, string id, string name, int expected, int completed, int onTime, int missed, int skipped = 0, int? groupId = null)
			=> new ChecklistComplianceGroup { Target = new ChecklistTarget { Type = type, Id = id, Name = name, GroupId = groupId }, Expected = expected, Completed = completed, OnTime = onTime, Missed = missed, Skipped = skipped };

		private void Order(int id, int? unitId, WorkOrderStatus status, WorkOrderPriority priority = WorkOrderPriority.Normal, DateTime? due = null)
			=> _orders.Add(new WorkOrderReportEntry { Order = new WorkOrderSummary { Id = id, UnitId = unitId, Status = status, Priority = priority, DueOn = due, CreatedOn = T0 } });

		private void Record(string id, int? callId, int unitId)
		{
			_records.Add(new RmsOperationalRecord { RmsOperationalRecordId = id, DepartmentId = Dept, DefinitionKey = RmsDefinitionKeys.Run, RecordType = 1, State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rev-" + id, StartedOn = T0, CallId = callId, CreatedOn = T0, FinalizedOn = T0, AuthorUserId = "u-a" });
			_units.Add(new RmsRecordUnitResponse { RecordId = id, RevisionId = "rev-" + id, DepartmentId = Dept, UnitId = unitId });
		}

		[Test]
		public async Task Readiness_joins_checklists_work_orders_equipment_and_evidence_on_the_unit_board()
		{
			_summary.Groups.AddRange(new[]
			{
				Group(ChecklistTargetType.Unit, "1", "Engine 1", 10, 9, 8, 1), Group(ChecklistTargetType.Unit, "2", "Medic 2", 10, 6, 5, 4, 1),
				Group(ChecklistTargetType.Group, "10", "Station 10", 4, 4, 4, 0), Group(ChecklistTargetType.Personnel, "u-a", "Ann Author", 3, 2, 2, 1), Group(ChecklistTargetType.InventoryAsset, "asset-1", "SCBA 12", 2, 2, 2, 0)
			});
			_summary.Trend.Add(new ChecklistMissedTrend { DayUtc = T0, Expected = 5, Missed = 1 });
			_summary.UnavailableSources.Add("contractor-deployments");
			Order(1, 1, WorkOrderStatus.InProgress, WorkOrderPriority.High, DateTime.UtcNow.AddDays(-2)); Order(2, 1, WorkOrderStatus.Closed); Order(3, 2, WorkOrderStatus.OnHold, WorkOrderPriority.Emergency); Order(4, null, WorkOrderStatus.Requested); Order(5, 3, WorkOrderStatus.Completed);
			_locations.Add(new InventoryLocation { Id = "loc-e1", DepartmentId = Dept, UnitId = 1 }); _locations.Add(new InventoryLocation { Id = "loc-bay", DepartmentId = Dept, ParentLocationId = "loc-e1" }); _locations.Add(new InventoryLocation { Id = "loc-store", DepartmentId = Dept, GroupId = 10 });
			_assets.Add(new InventoryAsset { Id = "a1", DepartmentId = Dept, Status = (int)InventoryAssetStatus.InService, CurrentLocationId = "loc-bay" });
			_assets.Add(new InventoryAsset { Id = "a2", DepartmentId = Dept, Status = (int)InventoryAssetStatus.OutForRepair, CurrentLocationId = "loc-e1" });
			_assets.Add(new InventoryAsset { Id = "a3", DepartmentId = Dept, Status = (int)InventoryAssetStatus.InService, CurrentLocationId = "loc-store", ExpiresOn = DateTime.UtcNow.AddDays(-1) });
			_assets.Add(new InventoryAsset { Id = "a4", DepartmentId = Dept, Status = (int)InventoryAssetStatus.Issued, CurrentLocationId = "loc-store", ExpiresOn = DateTime.UtcNow.AddDays(10) });
			_assets.Add(new InventoryAsset { Id = "a5", DepartmentId = Dept, Status = (int)InventoryAssetStatus.Retired, CurrentLocationId = "loc-store", IsDeleted = true });
			_issuances.Add(new InventoryIssuance { Id = "i1", DepartmentId = Dept, AssetId = "a4", IssuedToUnitId = 2, Status = (int)InventoryIssuanceStatus.Outstanding, ExpectedReturnOn = DateTime.UtcNow.AddDays(-3), IssuedOn = T0 });
			_issuances.Add(new InventoryIssuance { Id = "i2", DepartmentId = Dept, AssetId = "a1", IssuedToUnitId = 1, Status = (int)InventoryIssuanceStatus.Returned, ReturnedOn = T0, IssuedOn = T0 });
			Record("r1", 100, 1); Record("r2", 101, 2); Record("r3", null, 3);
			_headers.Add(new RmsEvidenceArtifactHeader { RmsEvidenceArtifactId = "e1", RecordId = "r1", Kind = 1, CapturedOn = T0.AddDays(1) });
			_headers.Add(new RmsEvidenceArtifactHeader { RmsEvidenceArtifactId = "e2", RecordId = "r1", Kind = 1, CapturedOn = T0, SupersededOn = T0.AddDays(1) });
			_headers.Add(new RmsEvidenceArtifactHeader { RmsEvidenceArtifactId = "e3", RecordId = "elsewhere", Kind = 1, CapturedOn = T0.AddDays(2) });

			var r = await _svc.GetReadinessAsync(Dept, Member, Q());

			r.ChecklistsEnabled.Should().BeTrue(); r.MaintenanceEnabled.Should().BeTrue(); r.InventoryEnabled.Should().BeTrue();
			r.ChecklistsForbidden.Should().BeFalse(); r.MaintenanceForbidden.Should().BeFalse(); r.InventoryForbidden.Should().BeFalse();

			var ck = r.Checklists;
			ck.CohortStart.Should().Be(Start, "a 62-day window fits inside the module's 93-day cohort");
			r.Warnings.Should().BeEmpty();
			ck.Expected.Should().Be(29); ck.Completed.Should().Be(23); ck.OnTime.Should().Be(21); ck.Missed.Should().Be(6); ck.Skipped.Should().Be(1);
			ck.CompletionRatePercent.Should().Be(79.3); ck.OnTimePercent.Should().Be(72.4);
			ck.ByUnit.Should().HaveCount(2); ck.ByUnit[0].Label.Should().Be("Medic 2", "worst first"); ck.ByUnit[0].Missed.Should().Be(4);
			ck.ByStationGroup.Single().Label.Should().Be("Station 10"); ck.ByPerson.Single().Label.Should().Be("Ann Author"); ck.ByAsset.Single().Label.Should().Be("SCBA 12");
			ck.Trend.Single().Missed.Should().Be(1); ck.UnavailableSources.Should().ContainSingle("contractor-deployments");

			var wo = r.WorkOrders;
			wo.Total.Should().Be(5); wo.Open.Should().Be(4); wo.Overdue.Should().Be(1); wo.OnHold.Should().Be(1);
			wo.MeanTimeToRepairHours.Should().Be(6.4); wo.RepairSamples.Should().Be(3);
			wo.OpenByPriority.Single(p => p.Label == "Emergency").Count.Should().Be(1);
			wo.OpenByAge[0].Label.Should().Be("0-6 days"); wo.OpenByAge[0].Count.Should().Be(4);
			wo.Costs.Single().Should().Match<RecordsWorkOrderCost>(c => c.Currency == "USD" && c.Total == 200.5m && c.Unknown == 1);
			wo.ByUnit.Should().HaveCount(3, "the order without a unit is not on the board");
			wo.ByUnit[0].Label.Should().Be("Engine 1", "the overdue unit sorts first"); wo.ByUnit[0].Open.Should().Be(1); wo.ByUnit[0].Total.Should().Be(2);
			wo.HistoryTruncated.Should().BeFalse();

			var eq = r.Equipment;
			eq.Assets.Should().Be(4, "a deleted asset is not counted"); eq.InService.Should().Be(2); eq.Issued.Should().Be(1); eq.OutForRepair.Should().Be(1);
			eq.Expired.Should().Be(1); eq.ExpiringWithin30Days.Should().Be(1); eq.IssuancesOutstanding.Should().Be(1); eq.IssuancesOverdueReturn.Should().Be(1);
			eq.ByStatus.Should().Contain(s => s.Label == "Out For Repair" && s.Count == 1);
			eq.ByUnit.Single(u => u.Label == "Engine 1").Assets.Should().Be(2, "a1 sits in a bay under the engine's location, a2 at the engine itself");
			eq.ByUnit.Single(u => u.Label == "Medic 2").Should().Match<RecordsEquipmentUnitRow>(u => u.Assets == 1 && u.Issued == 1 && u.OverdueReturns == 1, "a4 is issued to the medic");

			r.Evidence.PacketsCaptured.Should().Be(2, "the superseded packet is not current");
			r.Evidence.CallLinkedRecords.Should().Be(2); r.Evidence.RecordsWithPacket.Should().Be(1); r.Evidence.CoveragePercent.Should().Be(50);

			r.Units.Should().HaveCount(3);
			var e1 = r.Units.Single(u => u.UnitId == 1); var m2 = r.Units.Single(u => u.UnitId == 2); var b3 = r.Units.Single(u => u.UnitId == 3);
			e1.State.Should().Be((int)UnitStateTypes.Available); e1.StationGroupLabel.Should().Be("Station 10");
			e1.ChecklistExpected.Should().Be(10); e1.ChecklistMissed.Should().Be(1); e1.ChecklistCompletionRatePercent.Should().Be(90);
			e1.OpenWorkOrders.Should().Be(1); e1.OverdueWorkOrders.Should().Be(1); e1.AssetsAssigned.Should().Be(2); e1.AssetsOutForRepair.Should().Be(1); e1.ReadinessPackets.Should().Be(1);
			m2.State.Should().Be((int)UnitStateTypes.OutOfService); m2.OnHoldWorkOrders.Should().Be(1); m2.AssetsAssigned.Should().Be(1); m2.ReadinessPackets.Should().Be(0);
			b3.NeedsAttention.Should().BeFalse(); b3.StationGroupLabel.Should().BeNull();
			r.Units.Take(2).Should().OnlyContain(u => u.NeedsAttention, "units needing attention sort first");
			r.RecordsRead.Should().Be(3);
		}

		[Test]
		public async Task Modules_that_are_off_or_that_refuse_the_viewer_contribute_nothing()
		{
			_access.Setup(a => a.CanUseChecklistsAsync(Dept)).ReturnsAsync(false);
			_h.DisabledFlags.Add(FeatureFlagKeys.MaintenanceWorkOrders);
			_inventory.Setup(i => i.ListAsync<InventoryAsset>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ThrowsAsync(new InventoryException(403, "MembershipRequired"));

			var r = await _svc.GetReadinessAsync(Dept, Member, Q());

			r.ChecklistsEnabled.Should().BeFalse(); r.Checklists.Should().BeNull();
			r.MaintenanceEnabled.Should().BeFalse(); r.WorkOrders.Should().BeNull();
			r.InventoryEnabled.Should().BeTrue(); r.InventoryForbidden.Should().BeTrue(); r.Equipment.Should().BeNull();
			r.Warnings.Should().BeEmpty("a module's own refusal is a flag, not a warning");
			_checklists.Verify(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>()), Times.Never);
			_workOrders.Verify(w => w.GetWorkOrderStatsAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderReportQuery>()), Times.Never);

			_access.Setup(a => a.CanUseChecklistsAsync(Dept)).ReturnsAsync(true);
			_checklists.Setup(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>())).ThrowsAsync(new ChecklistException(403, "Checklist management permission is required."));
			_h.DisabledFlags.Clear();
			_workOrders.Setup(w => w.GetWorkOrderStatsAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderReportQuery>())).ThrowsAsync(new WorkOrderException(402, "ReadinessProRequired"));
			var again = await _svc.GetReadinessAsync(Dept, Member, Q());
			again.ChecklistsEnabled.Should().BeTrue(); again.ChecklistsForbidden.Should().BeTrue(); again.Checklists.Should().BeNull();
			again.MaintenanceEnabled.Should().BeFalse("a 402 from the module means the paid module is not available, not that the viewer is refused"); again.WorkOrders.Should().BeNull();
			again.Units.Should().HaveCount(3, "the unit board still lists the units with their states");
		}

		[Test]
		public async Task Group_scoped_viewer_sees_only_their_units_and_the_packets_on_records_they_can_open()
		{
			_h.Authorization.Setup(a => a.GetVisibleGroupIdsAsync(Member, Dept)).ReturnsAsync(new List<int> { 10 });
			_summary.Groups.AddRange(new[] { Group(ChecklistTargetType.Unit, "1", "Engine 1", 5, 5, 5, 0), Group(ChecklistTargetType.Unit, "2", "Medic 2", 5, 1, 1, 4), Group(ChecklistTargetType.Group, "20", "Station 20", 3, 0, 0, 3), Group(ChecklistTargetType.Personnel, "u-z", "Zed", 1, 0, 0, 1, groupId: 20) });
			Order(1, 2, WorkOrderStatus.InProgress, due: DateTime.UtcNow.AddDays(-1));
			Record("mine", 100, 1); _records[0].AuthorUserId = Member; Record("theirs", 101, 2);
			_headers.Add(new RmsEvidenceArtifactHeader { RmsEvidenceArtifactId = "e1", RecordId = "mine", Kind = 1, CapturedOn = T0 });
			_headers.Add(new RmsEvidenceArtifactHeader { RmsEvidenceArtifactId = "e2", RecordId = "theirs", Kind = 1, CapturedOn = T0 });
			_h.Records.Setup(r => r.GetFinalizedInRangeAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(() => _records.ToList());
			var scopes = new Mock<IRmsRecordGroupScopesRepository>(); scopes.Setup(s => s.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordGroupScope>());
			_svc = new RecordsAnalyticsService(_h.Gate, _h.Authorization.Object, _h.Records.Object, _h.Units.Object, _participants.Object, scopes.Object, Mock.Of<IRmsOperationalRecordDetailsRepository>(), _h.Reports.Object,
				Mock.Of<IRmsUnitResponsesRepository>(), Mock.Of<IRmsIncidentTypesRepository>(), Mock.Of<IRmsRevisionsRepository>(), Mock.Of<IRmsRecordDueStatesRepository>(), _h.Inspections, _h.Programs, _h.Violations, _h.Permits, _h.PermitTypes, _h.Hydrants, _h.FlowTests, _h.Occupancies, _h.Crr,
				_unitsService.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IDepartmentsService>(),
				_h.Flags.Object, _checklists.Object, _access.Object, _workOrders.Object, _inventory.Object, _inventoryAuth.Object, _evidence.Object);

			var r = await _svc.GetReadinessAsync(Dept, Member, Q());

			r.GroupScoped.Should().BeTrue();
			r.Units.Select(u => u.UnitId).Should().BeEquivalentTo(new[] { 1, 3 }, "station 20's medic is outside the viewer's scope; the unit without a group stays visible");
			r.Checklists.ByUnit.Should().ContainSingle(u => u.Key == "1");
			r.Checklists.ByStationGroup.Should().BeEmpty("station 20 is not visible");
			r.Checklists.ByPerson.Should().BeEmpty("the person target belongs to station 20");
			r.Checklists.Missed.Should().Be(0);
			r.WorkOrders.ByUnit.Should().BeEmpty("the only order sits on the invisible medic");
			r.WorkOrders.Overdue.Should().Be(1, "the module's own totals are what the module answered for this viewer");
			r.Evidence.PacketsCaptured.Should().Be(1, "only the packet on a Record the viewer can open");
			r.Evidence.CallLinkedRecords.Should().Be(1); r.Evidence.CoveragePercent.Should().Be(100);
			r.Units.Single(u => u.UnitId == 1).ReadinessPackets.Should().Be(1);
		}

		[Test]
		public async Task Checklist_cohort_is_the_tail_of_a_window_wider_than_93_days()
		{
			var wide = await _svc.GetReadinessAsync(Dept, Member, new RecordsAnalyticsQuery { Start = End.AddDays(-120), End = End });

			wide.Checklists.CohortStart.Should().Be(End.AddDays(-RecordsAnalyticsService.ChecklistCohortDays), "the module measures at most 93 days, so the cohort is the tail of the window");
			wide.Checklists.CohortEnd.Should().Be(End);
			wide.Warnings.Should().ContainSingle(w => w.Contains("93 days"));
			_checklists.Verify(c => c.GetComplianceSummaryAsync(It.Is<ChecklistActor>(a => a.DepartmentId == Dept && a.UserId == Member), It.Is<ChecklistReportQuery>(q => q.FromUtc == End.AddDays(-93) && q.UntilUtc == End)), Times.Once);
		}

		[Test]
		public async Task Inventory_pages_stop_at_the_cap_and_say_so()
		{
			for (var i = 0; i < RecordsAnalyticsService.InventoryPageCap * 500 + 1; i++) _assets.Add(new InventoryAsset { Id = "a" + i, DepartmentId = Dept, Status = (int)InventoryAssetStatus.InService });

			var r = await _svc.GetReadinessAsync(Dept, Member, Q());

			r.Equipment.Truncated.Should().BeTrue();
			r.Equipment.Assets.Should().Be(RecordsAnalyticsService.InventoryPageCap * 500);
			r.Warnings.Should().ContainSingle(w => w.Contains("Equipment covers the first"));
		}
	}
}
