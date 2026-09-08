using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6): response-time distributions, workload and personnel hours,
	/// the executive summary against the prior window, accreditation and community-risk sections, and the rules every
	/// dashboard shares - the flag, the viewer gate, group scoping, the window clamp and the row cap.
	/// </summary>
	[TestFixture]
	public class RecordsAnalyticsServiceTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime Start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime End = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

		private RmsPreventionHarness _h;
		private Mock<IRmsRecordParticipantsRepository> _participants;
		private Mock<IRmsRecordGroupScopesRepository> _scopes;
		private Mock<IRmsOperationalRecordDetailsRepository> _details;
		private Mock<IRmsUnitResponsesRepository> _reportUnits;
		private Mock<IRmsIncidentTypesRepository> _incidentTypes;
		private Mock<IRmsRevisionsRepository> _revisions;
		private Mock<IRmsRecordDueStatesRepository> _dueStates;
		private Mock<IUnitsService> _unitsService;
		private Mock<IDepartmentGroupsService> _groupsService;
		private Mock<IDepartmentsService> _departmentsService;
		private List<RmsOperationalRecord> _records;
		private List<RmsRecordUnitResponse> _units;
		private List<RmsRecordParticipant> _people;
		private List<RmsIncidentReport> _reports;
		private List<RmsUnitResponse> _reportUnitRows;
		private RecordsAnalyticsService _svc;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			_participants = new Mock<IRmsRecordParticipantsRepository>(); _scopes = new Mock<IRmsRecordGroupScopesRepository>(); _details = new Mock<IRmsOperationalRecordDetailsRepository>();
			_reportUnits = new Mock<IRmsUnitResponsesRepository>(); _incidentTypes = new Mock<IRmsIncidentTypesRepository>(); _revisions = new Mock<IRmsRevisionsRepository>(); _dueStates = new Mock<IRmsRecordDueStatesRepository>();
			_unitsService = new Mock<IUnitsService>(); _groupsService = new Mock<IDepartmentGroupsService>(); _departmentsService = new Mock<IDepartmentsService>();
			_records = new List<RmsOperationalRecord>(); _units = new List<RmsRecordUnitResponse>(); _people = new List<RmsRecordParticipant>(); _reports = new List<RmsIncidentReport>(); _reportUnitRows = new List<RmsUnitResponse>();

			_h.Records.Setup(r => r.GetFinalizedInRangeAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, IEnumerable<int> states, DateTime s, DateTime e, int take) => _records.Where(r => states.Contains(r.State) && (r.StartedOn ?? r.FinalizedOn) >= s && (r.StartedOn ?? r.FinalizedOn) < e).Take(take).ToList());
			_h.Records.Setup(r => r.CountVisibleAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<List<int>>(), It.IsAny<string>())).ReturnsAsync((int d, IEnumerable<int> states, List<int> g, string u) => states.Contains((int)RmsRecordState.Draft) ? 3 : 2);
			_h.Units.Setup(u => u.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _units.Where(u => set.Contains(u.RevisionId)).ToList(); });
			_participants.Setup(p => p.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _people.Where(p => set.Contains(p.RevisionId)).ToList(); });
			_scopes.Setup(s => s.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordGroupScope>());
			_details.Setup(d => d.GetCallContextForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => ids.Select(id => new RmsRecordCallContext { RevisionId = id, CallType = id.EndsWith("2") ? "EMS" : "Fire" }).ToList());
			_h.Reports.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsIncidentReportQuery>())).ReturnsAsync((int d, RmsIncidentReportQuery q) => _reports.Where(r => (r.CallCreatedOn ?? r.FinalizedOn) >= q.OccurredOnStart && (r.CallCreatedOn ?? r.FinalizedOn) < q.OccurredOnEnd).ToList());
			_reportUnits.Setup(u => u.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _reportUnitRows.Where(u => set.Contains(u.RevisionId)).ToList(); });
			_incidentTypes.Setup(t => t.GetForRevisionsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => ids.SelectMany(id => new[] { new RmsIncidentType { RevisionId = id, TypeCode = "FIRE||STRUCTURE", IsPrimary = false, Ordinal = 0 }, new RmsIncidentType { RevisionId = id, TypeCode = "MEDICAL||CARDIAC", IsPrimary = true, Ordinal = 1 } }).ToList());
			_revisions.Setup(r => r.GetTransitionsInRangeAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<RmsRevisionTransitionRow>());
			_dueStates.Setup(d => d.GetLastEmittedInRangeAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<RmsRecordDueState>());
			_dueStates.Setup(d => d.CountVisibleOverdueAsync(Dept, It.IsAny<List<int>>(), It.IsAny<string>())).ReturnsAsync(4);
			_unitsService.Setup(u => u.GetUnitsForDepartmentAsync(Dept)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 1, Name = "Engine 1" }, new Unit { UnitId = 2, Name = "Medic 2" } });
			_groupsService.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 10, Name = "Station 10" } });
			_departmentsService.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "u-a", FirstName = "Ann", LastName = "Author" } });
			_departmentsService.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "UTC" });

			_svc = new RecordsAnalyticsService(_h.Gate, _h.Authorization.Object, _h.Records.Object, _h.Units.Object, _participants.Object, _scopes.Object, _details.Object, _h.Reports.Object, _reportUnits.Object,
				_incidentTypes.Object, _revisions.Object, _dueStates.Object, _h.Inspections, _h.Programs, _h.Violations, _h.Permits, _h.PermitTypes, _h.Hydrants, _h.FlowTests, _h.Occupancies, _h.Crr,
				_unitsService.Object, _groupsService.Object, _departmentsService.Object);
		}

		private static RecordsAnalyticsQuery Q() => new RecordsAnalyticsQuery { Start = Start, End = End };

		private RmsOperationalRecord Record(string id, string definition = RmsDefinitionKeys.Run, DateTime? started = null, double hours = 2, string author = "u-a", int? group = 10, int? callId = 1, int returns = 0)
		{
			var s = started ?? T0;
			var r = new RmsOperationalRecord { RmsOperationalRecordId = id, DepartmentId = Dept, DefinitionKey = definition, RecordType = (int)RmsDefinitionKeys.LockedTypes[definition], State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rev-" + id, StartedOn = s, EndedOn = s.AddHours(hours), AuthorUserId = author, OwnerUserId = author, StationGroupId = group, CallId = callId, CreatedOn = s.AddHours(-1), SubmittedForReviewOn = s.AddHours(hours + 1), ApprovedOn = s.AddHours(hours + 3), FinalizedOn = s.AddHours(hours + 4), ReturnCount = returns };
			_records.Add(r);
			return r;
		}

		private void UnitTimes(string recordId, int unitId, int turnoutSeconds, int travelSeconds, int onSceneMinutes, DateTime? dispatched = null, int? group = 10)
		{
			var d = dispatched ?? T0;
			_units.Add(new RmsRecordUnitResponse { RecordId = recordId, RevisionId = "rev-" + recordId, DepartmentId = Dept, UnitId = unitId, UnitNameSnapshot = "Unit " + unitId, StationGroupIdSnapshot = group, Dispatched = d, Enroute = d.AddSeconds(turnoutSeconds), OnScene = d.AddSeconds(turnoutSeconds + travelSeconds), Released = d.AddSeconds(turnoutSeconds + travelSeconds).AddMinutes(onSceneMinutes) });
		}

		private void Person(string recordId, string userId, DateTime? from = null, DateTime? to = null)
			=> _people.Add(new RmsRecordParticipant { RecordId = recordId, RevisionId = "rev-" + recordId, DepartmentId = Dept, UserId = userId, ParticipationStart = from, ParticipationEnd = to });

		[Test]
		public async Task Response_performance_computes_percentiles_targets_and_first_arrival_and_drops_garbage_times()
		{
			Record("r1"); Record("r2"); Record("r3");
			UnitTimes("r1", 1, 60, 200, 30); UnitTimes("r1", 2, 90, 300, 20);
			UnitTimes("r2", 1, 70, 250, 40); UnitTimes("r2", 2, 120, 400, 10);
			UnitTimes("r3", 1, 80, 230, 15);
			// Reversed and absurd intervals never enter a distribution.
			_units.Add(new RmsRecordUnitResponse { RecordId = "r3", RevisionId = "rev-r3", DepartmentId = Dept, UnitId = 2, Dispatched = T0, Enroute = T0.AddSeconds(-30), OnScene = T0.AddDays(3) });
			var q = Q(); q.TurnoutTargetSeconds = 80; q.TravelTargetSeconds = 240;

			var p = await _svc.GetResponsePerformanceAsync(Dept, Member, q);

			p.Responses.Should().Be(5, "the sixth unit row has no usable interval");
			p.RecordsWithResponses.Should().Be(3);
			p.Turnout.Samples.Should().Be(5);
			p.Turnout.P90Seconds.Should().Be(120, "nearest-rank 90th of 60,70,80,90,120");
			p.Turnout.MedianSeconds.Should().Be(80);
			p.Turnout.WithinTarget.Should().Be(3, "60, 70 and 80 are within 80 s");
			p.Turnout.WithinTargetPercent.Should().Be(60);
			p.Travel.P90Seconds.Should().Be(400);
			p.TotalResponse.TargetSeconds.Should().Be(320);
			p.TotalResponse.P90Seconds.Should().Be(520);
			p.OnScene.MedianSeconds.Should().Be(20 * 60);
			p.FirstArrival.Samples.Should().Be(3);
			p.FirstArrival.MedianSeconds.Should().Be(310, "first arrivals are r1 260 s, r2 320 s and r3 310 s");
			p.ByUnit.Should().HaveCount(2);
			p.ByUnit.First(u => u.Key == "1").Label.Should().Be("Engine 1", "unit names resolve through the units service");
			p.ByUnit.First(u => u.Key == "1").Responses.Should().Be(3);
			p.ByStationGroup.Single().Label.Should().Be("Station 10");
			p.ByHourOfDay.Should().HaveCount(24);
			p.ByHourOfDay.Single(h => h.Responses > 0).Key.Should().Be("00");
			p.ByMonth.Single().Key.Should().Be("2026-08");
			p.BySource.Single().Key.Should().Be("operational");
			p.GroupScoped.Should().BeFalse();
			p.Truncated.Should().BeFalse();
			p.RecordsRead.Should().Be(3);
		}

		[Test]
		public async Task Incident_reports_join_the_distributions_and_first_arrival_uses_the_earliest_unit()
		{
			_reports.Add(new RmsIncidentReport { RmsIncidentReportId = "i1", DepartmentId = Dept, State = (int)RmsRecordState.Accepted, CurrentRevisionId = "rev-i1", CallCreatedOn = T0, FinalizedOn = T0.AddDays(1), AcceptedOn = T0.AddDays(2), StationGroupId = 10, CreatedOn = T0 });
			_reportUnitRows.Add(new RmsUnitResponse { RecordId = "i1", RevisionId = "rev-i1", DepartmentId = Dept, UnitId = 1, DispatchedOn = T0, EnrouteOn = T0.AddSeconds(50), OnSceneOn = T0.AddSeconds(350), ClearedOn = T0.AddMinutes(45) });
			_reportUnitRows.Add(new RmsUnitResponse { RecordId = "i1", RevisionId = "rev-i1", DepartmentId = Dept, UnitId = 2, DispatchedOn = T0.AddSeconds(10), EnrouteOn = T0.AddSeconds(100), OnSceneOn = T0.AddSeconds(500) });
			_reportUnitRows.Add(new RmsUnitResponse { RecordId = "i1", RevisionId = "rev-i1", DepartmentId = Dept, UnitId = 3, UnableToDispatch = true, DispatchedOn = T0, EnrouteOn = T0.AddSeconds(5) });

			var p = await _svc.GetResponsePerformanceAsync(Dept, Member, Q());

			p.Responses.Should().Be(2, "an unable-to-dispatch unit is not a response");
			p.FirstArrival.Samples.Should().Be(1);
			p.FirstArrival.MedianSeconds.Should().Be(350, "earliest on-scene minus earliest dispatch");
			p.BySource.Single().Key.Should().Be("incident");
			p.OnScene.Samples.Should().Be(1);
			p.RecordsRead.Should().Be(1);
		}

		[Test]
		public async Task Workload_totals_hours_by_person_definition_unit_and_heat_map()
		{
			Record("r1", RmsDefinitionKeys.Training, T0, 3); Record("r2", RmsDefinitionKeys.Run, T0.AddDays(1).AddHours(14), 1.5); Record("r3", RmsDefinitionKeys.Work, T0.AddDays(-1), 4, author: "u-b");
			Person("r1", "u-a"); Person("r1", "u-b", T0, T0.AddHours(1)); Person("r2", "u-a"); Person("r3", "u-b");
			UnitTimes("r2", 1, 60, 240, 30);
			_reports.Add(new RmsIncidentReport { RmsIncidentReportId = "i1", DepartmentId = Dept, State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rev-i1", CallCreatedOn = T0.AddDays(2), FinalizedOn = T0.AddDays(3), CreatedOn = T0 });

			var w = await _svc.GetWorkloadAsync(Dept, Member, Q());

			w.RecordsFinalized.Should().Be(3);
			w.IncidentReportsFinalized.Should().Be(1);
			w.RecordHours.Should().Be(8.5);
			w.PersonnelHours.Should().Be(3 + 1 + 1.5 + 4, "a participant span wins over the record duration when it exists");
			w.TrainingHours.Should().Be(4, "both training participants: 3 h record duration plus a 1 h span");
			w.UnitResponses.Should().Be(1);
			w.UnitOnSceneHours.Should().Be(0.5);
			w.ByDefinition.Should().HaveCount(4).And.Contain(d => d.Key == RmsDefinitionKeys.Training && d.Label == "Training" && d.Hours == 3);
			w.ByDefinition.Should().Contain(d => d.Key == RmsDefinitionKeys.NerisIncidentReport && d.Count == 1);
			w.ByAuthor.First(a => a.Key == "u-a").Label.Should().Be("Ann Author");
			w.ByAuthor.First(a => a.Key == "u-a").Count.Should().Be(2);
			w.ByPerson.First(a => a.Key == "u-b").Hours.Should().Be(5);
			w.ByUnit.Single().Label.Should().Be("Engine 1");
			w.ByMonth.Should().ContainSingle(m => m.Key == "2026-07" && m.Count == 1).And.ContainSingle(m => m.Key == "2026-08" && m.Count == 3);
			w.ByWeekdayHour.Should().HaveCount(7 * 24);
			w.ByWeekdayHour.Single(c => c.Weekday == (int)T0.AddDays(1).DayOfWeek && c.Hour == 14).Count.Should().Be(1);
			w.ByWeekdayHour.Sum(c => c.Count).Should().Be(4);
		}

		[Test]
		public async Task Executive_summary_compares_with_the_prior_window_and_reads_lifecycle_and_queues()
		{
			Record("r1", started: T0, returns: 1); Record("r2", started: T0.AddDays(3)); Record("old", started: Start.AddDays(-10));
			Person("r1", "u-a"); Person("r2", "u-a");
			_reports.Add(new RmsIncidentReport { RmsIncidentReportId = "i1", DepartmentId = Dept, State = (int)RmsRecordState.Accepted, CurrentRevisionId = "rev-i1", CallCreatedOn = T0, FinalizedOn = T0.AddDays(1), AcceptedOn = T0.AddDays(2), CreatedOn = T0 });
			_reports.Add(new RmsIncidentReport { RmsIncidentReportId = "i2", DepartmentId = Dept, State = (int)RmsRecordState.Rejected, CurrentRevisionId = "rev-i2", CallCreatedOn = T0, FinalizedOn = T0.AddDays(1), RejectedOn = T0.AddDays(2), CreatedOn = T0 });
			_revisions.Setup(r => r.GetTransitionsInRangeAsync(Dept, Start, End, It.IsAny<int>())).ReturnsAsync(new List<RmsRevisionTransitionRow> { new RmsRevisionTransitionRow { RecordId = "r1", Transition = (int)RmsRevisionTransition.Amended }, new RmsRevisionTransitionRow { RecordId = "r2", Transition = (int)RmsRevisionTransition.Voided } });
			_dueStates.Setup(d => d.GetLastEmittedInRangeAsync(Dept, Start, End, It.IsAny<int>())).ReturnsAsync(new List<RmsRecordDueState>
			{
				new RmsRecordDueState { RecordId = "r1", OverdueCount = 1, LastEmittedState = (int)RmsDueState.Overdue, LastEmittedOn = T0 },
				new RmsRecordDueState { RecordId = "r2", OverdueCount = 0, LastEmittedState = (int)RmsDueState.DueSoon, LastEmittedOn = T0 },
				new RmsRecordDueState { RecordId = "old", OverdueCount = 2, LastEmittedState = (int)RmsDueState.Cleared, LastEmittedOn = Start.AddDays(-1) }
			});

			var s = await _svc.GetExecutiveSummaryAsync(Dept, Member, Q());

			s.PriorStart.Should().Be(Start - (End - Start)); s.PriorEnd.Should().Be(Start);
			var finalized = s.Kpis.Single(k => k.Key == "finalized");
			finalized.Current.Should().Be(2); finalized.Prior.Should().Be(1); finalized.ChangePercent.Should().Be(100);
			s.Kpis.Single(k => k.Key == "incidentReports").Current.Should().Be(2);
			s.Kpis.Single(k => k.Key == "nerisAccepted").Current.Should().Be(1);
			s.Kpis.Single(k => k.Key == "nerisRejected").Current.Should().Be(1);
			s.Kpis.Single(k => k.Key == "amended").Current.Should().Be(1);
			s.Kpis.Single(k => k.Key == "voided").Current.Should().Be(1);
			s.Kpis.Single(k => k.Key == "personnelHours").Current.Should().Be(4);
			s.Kpis.Single(k => k.Key == "personnelHours").ChangePercent.Should().BeNull("a zero prior figure has no percentage change");
			s.MedianHoursToFinalize.Should().Be(7, "created 1 h before start, finalized 2 h after a 2 h record ends");
			s.MedianReviewHours.Should().Be(2);
			s.ReturnRatePercent.Should().Be(50);
			s.NerisAcceptanceRatePercent.Should().Be(50);
			s.OpenDrafts.Should().Be(3); s.AwaitingReview.Should().Be(2); s.OverdueNow.Should().Be(4);
			s.WentOverdue.Should().Be(1, "only the row that went overdue inside the window counts");
			s.ByMonth.Should().ContainSingle(m => m.Key == "2026-08" && m.Count == 4);
		}

		[Test]
		public async Task Group_scoped_viewer_only_counts_records_they_could_open_and_passes_scope_to_the_incident_query()
		{
			_h.Authorization.Setup(a => a.GetVisibleGroupIdsAsync(Member, Dept)).ReturnsAsync(new List<int> { 10 });
			Record("mine", author: Member, group: 99); Record("theirs", author: "u-z", group: 99); Record("scoped", author: "u-z", group: 10); Record("shared", author: "u-z", group: 99);
			Person("shared", Member);
			_scopes.Setup(s => s.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordGroupScope> { new RmsRecordGroupScope { RecordId = "scoped", DepartmentGroupId = 10 } });
			UnitTimes("mine", 1, 60, 200, 10); UnitTimes("theirs", 1, 60, 200, 10); UnitTimes("scoped", 1, 60, 200, 10); UnitTimes("shared", 1, 60, 200, 10);
			RmsIncidentReportQuery captured = null;
			_h.Reports.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsIncidentReportQuery>())).Callback((int d, RmsIncidentReportQuery q) => captured = q).ReturnsAsync(new List<RmsIncidentReport>());

			var w = await _svc.GetWorkloadAsync(Dept, Member, Q());

			w.GroupScoped.Should().BeTrue();
			w.RecordsFinalized.Should().Be(3, "the author's own, the participant's and the group-scoped record; never the fourth");
			w.UnitResponses.Should().Be(3);
			captured.VisibleGroupIds.Should().BeEquivalentTo(new[] { 10 });
			captured.ViewerUserId.Should().Be(Member);
			captured.States.Should().Contain((int)RmsRecordState.Accepted);
		}

		[Test]
		public async Task Gate_flag_viewer_window_clamp_and_row_cap_apply_to_every_dashboard()
		{
			Func<Task> outsider = () => _svc.GetWorkloadAsync(Dept, Outsider, Q());
			await outsider.Should().ThrowAsync<UnauthorizedAccessException>();

			_h.DisabledFlags.Add(FeatureFlagKeys.RecordsAnalytics);
			(await _svc.IsModuleEnabledAsync(Dept)).Should().BeFalse();
			Func<Task> off = () => _svc.GetExecutiveSummaryAsync(Dept, Admin, Q());
			await off.Should().ThrowAsync<RecordsModuleDisabledException>();
			_h.DisabledFlags.Clear();

			var wide = await _svc.GetWorkloadAsync(Dept, Member, new RecordsAnalyticsQuery { Start = End.AddDays(-800), End = End });
			(wide.End - wide.Start).TotalDays.Should().Be(RecordsAnalyticsLimits.MaxWindowDays);
			wide.Warnings.Should().ContainSingle(w => w.Contains("clamped"));

			for (var i = 0; i < RecordsAnalyticsLimits.RowCap + 1; i++) _records.Add(new RmsOperationalRecord { RmsOperationalRecordId = "x" + i, DepartmentId = Dept, DefinitionKey = RmsDefinitionKeys.Run, RecordType = 1, State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rx" + i, StartedOn = T0, CreatedOn = T0, FinalizedOn = T0 });
			var capped = await _svc.GetWorkloadAsync(Dept, Member, Q());
			capped.Truncated.Should().BeTrue();
			capped.RecordsFinalized.Should().Be(RecordsAnalyticsLimits.RowCap);
			capped.Warnings.Should().ContainSingle(w => w.Contains("Narrow the window"));
		}

		[Test]
		public async Task Accreditation_reads_prevention_history_only_for_enabled_modules_and_training_from_records()
		{
			var occ = _h.SeedOccupancy(); occ.NextReviewDue = DateTime.UtcNow.AddMonths(3); occ.LastInspectedOn = DateTime.UtcNow.AddMonths(-2); occ.HazmatOnSite = true;
			var stale = _h.SeedOccupancy("Old Barn", "1 Farm Ln", occupancyType: 5); stale.NextReviewDue = DateTime.UtcNow.AddMonths(-1); stale.SprinklerType = (int)RmsSprinklerType.None;
			_h.Programs.Rows.Add(new RmsInspectionProgram { RmsInspectionProgramId = "prog", DepartmentId = Dept, Name = "Annual", IsActive = true });
			_h.Inspections.Rows.Add(new RmsInspection { RmsInspectionId = "i1", DepartmentId = Dept, RmsOccupancyId = occ.RmsOccupancyId, RmsInspectionProgramId = "prog", State = (int)RmsInspectionState.Completed, Result = (int)RmsInspectionResult.Pass, ScheduledOn = T0.AddDays(2), CompletedOn = T0, CreatedOn = T0 });
			_h.Inspections.Rows.Add(new RmsInspection { RmsInspectionId = "i2", DepartmentId = Dept, RmsOccupancyId = occ.RmsOccupancyId, RmsInspectionProgramId = "prog", State = (int)RmsInspectionState.ReinspectionRequired, Result = (int)RmsInspectionResult.Fail, ScheduledOn = T0, CompletedOn = T0.AddDays(3), CreatedOn = T0 });
			_h.Inspections.Rows.Add(new RmsInspection { RmsInspectionId = "i3", DepartmentId = Dept, RmsOccupancyId = stale.RmsOccupancyId, State = (int)RmsInspectionState.Scheduled, ScheduledOn = DateTime.UtcNow.AddDays(-5), CreatedOn = T0 });
			_h.Violations.Rows.Add(new RmsViolation { RmsViolationId = "v1", DepartmentId = Dept, RmsOccupancyId = occ.RmsOccupancyId, Severity = (int)RmsViolationSeverity.Serious, State = (int)RmsViolationState.Verified, CreatedOn = T0, CorrectedOn = T0.AddDays(10), DueOn = T0.AddDays(30) });
			_h.Violations.Rows.Add(new RmsViolation { RmsViolationId = "v2", DepartmentId = Dept, RmsOccupancyId = occ.RmsOccupancyId, Severity = (int)RmsViolationSeverity.Minor, State = (int)RmsViolationState.Open, CreatedOn = T0, DueOn = DateTime.UtcNow.AddDays(-1) });
			_h.PermitTypes.Rows.Add(new RmsPermitType { RmsPermitTypeId = "pt", DepartmentId = Dept, Name = "Hot work", IsActive = true });
			_h.Permits.Rows.Add(new RmsPermit { RmsPermitId = "p1", DepartmentId = Dept, RmsPermitTypeId = "pt", State = (int)RmsPermitState.Issued, AppliedOn = T0, IssuedOn = T0.AddDays(4), ExpiresOn = DateTime.UtcNow.AddYears(1), CreatedOn = T0 });
			_h.Permits.Rows.Add(new RmsPermit { RmsPermitId = "p2", DepartmentId = Dept, RmsPermitTypeId = "pt", State = (int)RmsPermitState.Denied, AppliedOn = T0, CreatedOn = T0 });
			_h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "h1", DepartmentId = Dept, InService = true, LastTestedOn = DateTime.UtcNow.AddMonths(-3), FlowClass = (int)RmsHydrantFlowClass.A, CreatedOn = T0 });
			_h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "h2", DepartmentId = Dept, InService = false, LastTestedOn = DateTime.UtcNow.AddYears(-2), FlowClass = (int)RmsHydrantFlowClass.C, CreatedOn = T0 });
			_h.FlowTests.Rows.Add(new RmsHydrantFlowTest { RmsHydrantFlowTestId = "ft", DepartmentId = Dept, RmsHydrantId = "h1", TestedOn = T0, CreatedOn = T0 });
			Record("t1", RmsDefinitionKeys.Training, T0, 2); Record("t2", RmsDefinitionKeys.Training, T0.AddDays(1), 3);
			Person("t1", "u-a"); Person("t1", "u-b"); Person("t2", "u-a");
			UnitTimes("t1", 1, 60, 200, 10);

			var a = await _svc.GetAccreditationAsync(Dept, Member, Q());

			a.InspectionsEnabled.Should().BeTrue(); a.Inspections.Completed.Should().Be(2); a.Inspections.Passed.Should().Be(1); a.Inspections.Failed.Should().Be(1); a.Inspections.PassRatePercent.Should().Be(50);
			a.Inspections.CompletedOnSchedule.Should().Be(1); a.Inspections.OnSchedulePercent.Should().Be(50); a.Inspections.AverageDaysScheduledToCompleted.Should().Be(0.5, "(-2 + 3) / 2");
			a.Inspections.ReinspectionsRequired.Should().Be(1); a.Inspections.OpenNow.Should().Be(2); a.Inspections.OverdueNow.Should().Be(1);
			a.Inspections.ByProgram.Single().Label.Should().Be("Annual");
			a.Violations.Opened.Should().Be(2); a.Violations.Corrected.Should().Be(1); a.Violations.Verified.Should().Be(1); a.Violations.AverageDaysToCorrection.Should().Be(10); a.Violations.OpenNow.Should().Be(1); a.Violations.OverdueNow.Should().Be(1);
			a.Violations.BySeverity.Should().Contain(s => s.Label == "Serious" && s.Count == 1);
			a.Permits.Applied.Should().Be(2); a.Permits.Issued.Should().Be(1); a.Permits.Denied.Should().Be(1); a.Permits.ActiveNow.Should().Be(1); a.Permits.AverageDaysAppliedToIssued.Should().Be(4); a.Permits.ByType.Single().Label.Should().Be("Hot work");
			a.Hydrants.Total.Should().Be(2); a.Hydrants.InService.Should().Be(1); a.Hydrants.TestedWithin12Months.Should().Be(1); a.Hydrants.TestedWithin12MonthsPercent.Should().Be(50); a.Hydrants.FlowTestsInWindow.Should().Be(1);
			a.Hydrants.ByFlowClass.Should().Contain(c => c.Label == "Class A");
			a.Occupancies.Total.Should().Be(2); a.Occupancies.ReviewCurrent.Should().Be(1); a.Occupancies.ReviewOverdue.Should().Be(1); a.Occupancies.InspectedWithin12Months.Should().Be(1); a.Occupancies.HazmatOnSite.Should().Be(1); a.Occupancies.NoSprinklers.Should().Be(1); a.Occupancies.OpenViolations.Should().Be(1);
			a.Training.Records.Should().Be(2); a.Training.Hours.Should().Be(7); a.Training.MembersTrained.Should().Be(2); a.Training.HoursPerMember.Should().Be(3.5);
			a.Response.Responses.Should().Be(1); a.Response.TurnoutP90Seconds.Should().Be(60);

			_h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionInspections); _h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionPermits);
			var off = await _svc.GetAccreditationAsync(Dept, Member, Q());
			off.InspectionsEnabled.Should().BeFalse(); off.Inspections.Should().BeNull(); off.Violations.Should().BeNull(); off.Permits.Should().BeNull();
			off.Hydrants.Should().NotBeNull(); off.Occupancies.Should().NotBeNull();
			off.Occupancies.OpenViolations.Should().Be(0, "violations are read only while the inspections module is on");
		}

		[Test]
		public async Task Community_risk_sums_crr_and_classifies_incidents_by_primary_type_and_call_type()
		{
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "c1", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation, OccurredOn = T0, AudienceCount = 12, SmokeAlarmsInstalled = 9, HoursSpent = 2.5m, CreatedOn = T0 });
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "c2", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SchoolProgram, OccurredOn = T0.AddDays(20), AudienceCount = 200, HoursSpent = 4m, CreatedOn = T0 });
			_h.Crr.Rows.Add(new RmsCrrActivity { RmsCrrActivityId = "c3", DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SchoolProgram, OccurredOn = Start.AddDays(-2), AudienceCount = 999, CreatedOn = T0 });
			Record("r1", started: T0.AddHours(9)); Record("r2", started: T0.AddHours(21)); Record("w1", RmsDefinitionKeys.Work, T0, callId: null);
			_reports.Add(new RmsIncidentReport { RmsIncidentReportId = "i1", DepartmentId = Dept, State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rev-i1", CallCreatedOn = T0.AddHours(9), FinalizedOn = T0.AddDays(1), CreatedOn = T0 });
			_h.Hydrants.Rows.Add(new RmsHydrant { RmsHydrantId = "h1", DepartmentId = Dept, InService = false, FlowClass = (int)RmsHydrantFlowClass.B, CreatedOn = T0 });

			var r = await _svc.GetCommunityRiskAsync(Dept, Member, Q());

			r.Crr.Activities.Should().Be(2); r.Crr.Audience.Should().Be(212); r.Crr.SmokeAlarmsInstalled.Should().Be(9); r.Crr.Hours.Should().Be(6.5);
			r.Crr.ByKind.Should().Contain(k => k.Label == "Smoke Alarm Installation" && k.Count == 1 && k.Hours == 2.5);
			r.Incidents.IncidentReports.Should().Be(1); r.Incidents.RunRecords.Should().Be(2, "only records linked to a call carry a call type");
			r.Incidents.ByIncidentType.Single().Should().Match<RecordsAnalyticsCount>(c => c.Label == "MEDICAL / CARDIAC" && c.Count == 1);
			r.Incidents.ByCallType.Should().Contain(c => c.Key == "EMS" && c.Count == 1).And.Contain(c => c.Key == "Fire" && c.Count == 1);
			r.Incidents.ByHourOfDay.Single(h => h.Key == "09").Count.Should().Be(2);
			r.Incidents.ByMonth.Single().Count.Should().Be(4);
			r.Hydrants.OutOfService.Should().Be(1);
			r.OccupancyEnabled.Should().BeTrue(); r.Occupancies.Total.Should().Be(0);
		}
	}
}
