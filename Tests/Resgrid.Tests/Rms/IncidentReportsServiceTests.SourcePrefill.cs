using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Neris;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Report-source prefill on the incident report: every unit that worked the call (not only dispatched ones), staffing
	/// from the crew recorded with unit statuses, who set a status deciding its provenance (crew/app, dispatcher, Incident
	/// Command), NERIS tactic timestamps and mutual aid from Incident Command, the dispatcher-entered incident number, and a
	/// refresh that fills what arrived later without touching the author's corrections.
	/// </summary>
	public partial class IncidentReportsServiceTests
	{
		private Mock<IActionLogsService> _actionLogs;
		private Mock<ICustomStateService> _customStates;
		private Mock<IDepartmentsService> _departments;
		private Mock<IIncidentCommandService> _commands;
		private Mock<IIncidentResourcesService> _resources;
		private Mock<ICheckInTimerService> _checkIns;
		private Mock<IUnitStateRoleRepository> _crew;

		/// <summary>The feed's source data comes from the real service over this fixture's mocks, so prefill tests exercise both.</summary>
		private void WireCallSources()
		{
			_actionLogs = new Mock<IActionLogsService>();
			_actionLogs.Setup(a => a.GetActionLogsForCallAsync(Dept, CallId)).ReturnsAsync(new List<ActionLog>());
			_customStates = new Mock<ICustomStateService>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "crew-a", FirstName = "Ann", LastName = "Able" },
				new PersonName { UserId = "crew-b", FirstName = "Ben", LastName = "Baker" },
				new PersonName { UserId = "dispatcher", FirstName = "Dee", LastName = "Spatch" }
			});
			_commands = new Mock<IIncidentCommandService>();
			_resources = new Mock<IIncidentResourcesService>();
			_checkIns = new Mock<ICheckInTimerService>();
			_crew = new Mock<IUnitStateRoleRepository>();
			_crew.Setup(c => c.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>())).ReturnsAsync(new List<UnitStateRole>());
			_feeds.Setup(f => f.GetCallSourceDataAsync(Dept, It.IsAny<Call>())).Returns((int d, Call c) =>
				new CallSourceDataService(_calls.Object, _units.Object, _actionLogs.Object, _customStates.Object, _departments.Object, _commands.Object,
					_resources.Object, _checkIns.Object, _crew.Object).GetForCallAsync(d, c));
		}

		private void RunUnderCommand(params TacticalObjective[] objectives)
		{
			_commands.Setup(c => c.GetCommandForCallAsync(Dept, CallId)).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "cmd-1", DepartmentId = Dept, CallId = CallId, Name = "Main St Command", EstablishedOn = LoggedOn.AddMinutes(4), EstablishedByUserId = "crew-a"
			});
			_commands.Setup(c => c.GetObjectivesForCallAsync(Dept, CallId)).ReturnsAsync(objectives.ToList());
		}

		/// <summary>Section bodies keep their times as ISO strings; a default parse would turn them into culture-formatted dates.</summary>
		private static JObject Body(string json) =>
			JObject.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });

		private static TacticalObjective Completed(string id, string name, DateTime on) =>
			new TacticalObjective { TacticalObjectiveId = id, DepartmentId = Dept, CallId = CallId, Name = name, Status = (int)TacticalObjectiveStatus.Complete, CompletedOn = on, CompletedByUserId = "crew-a" };

		[Test]
		public async Task Start_from_call_includes_a_unit_that_worked_the_call_without_being_dispatched()
		{
			_units.Setup(u => u.GetUnitByIdAsync(6)).ReturnsAsync(new Unit { UnitId = 6, DepartmentId = Dept, Name = "Truck 6", Type = "Truck", StationGroupId = 13 });
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitStateId = 2, UnitId = 6, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(14), DestinationSource = (int)StatusDestinationSources.Explicit }
			});

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var truck = aggregate.Units.Should().ContainSingle(u => u.UnitId == 6).Subject;
			truck.OnSceneOn.Should().Be(LoggedOn.AddMinutes(14));
			truck.DispatchedOn.Should().BeNull("the truck self-assigned; nothing invents a dispatch time");
			aggregate.Units.Should().Contain(u => u.UnitId == 5);
		}

		[Test]
		public async Task Start_from_call_sets_staffing_from_the_crew_recorded_with_the_units_statuses()
		{
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 100, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitStateId = 101, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10) }
			});
			_crew.Setup(c => c.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>())).ReturnsAsync(new List<UnitStateRole>
			{
				new UnitStateRole { UnitStateId = 100, UserId = "crew-a", Role = "Officer" },
				new UnitStateRole { UnitStateId = 100, UserId = "crew-b", Role = "Driver" },
				new UnitStateRole { UnitStateId = 101, UserId = "CREW-A", Role = "Officer" }
			});
			// A third member placed on the unit by its status (rider), with no seat role.
			_actionLogs.Setup(a => a.GetActionLogsForCallAsync(Dept, CallId)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { ActionLogId = 9, UserId = "crew-c", DepartmentId = Dept, ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 101, Timestamp = LoggedOn.AddMinutes(10), DestinationSource = (int)StatusDestinationSources.Unit }
			});

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			aggregate.Units.Single(u => u.UnitId == 5).Staffing.Should().Be(3, "two seats and one rider, the officer counted once");
			var staffing = aggregate.Facts.Single(f => f.FactKey == IncidentSourceFactKeys.UnitStaffing(5));
			staffing.SourceValue.Should().Be("3");
			staffing.SourceSystem.Should().Be("UnitStateRoles");
		}

		[Test]
		public async Task Who_set_a_unit_status_decides_the_provenance_of_its_time()
		{
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2), SetByUserId = "crew-a", SetByOrigin = (int)StatusSetOrigins.UnitApp },
				new UnitState { UnitStateId = 2, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10), SetByUserId = "dispatcher", SetByOrigin = (int)StatusSetOrigins.DispatchApp },
				new UnitState { UnitStateId = 3, UnitId = 5, State = (int)UnitStateTypes.Released, Timestamp = LoggedOn.AddMinutes(50), SetByUserId = "crew-b", SetByOrigin = (int)StatusSetOrigins.CommandApp }
			});

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var facts = aggregate.Facts;
			facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "enroute_to_scene")).SourceKind.Should().Be((int)RmsSourceKind.App);
			var onScene = facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "on_scene"));
			onScene.SourceKind.Should().Be((int)RmsSourceKind.Dispatch, "a dispatcher entered it for the unit");
			onScene.SourceSystem.Should().Be("UnitStates (dispatcher)");
			var clear = facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "unit_clear"));
			clear.SourceKind.Should().Be((int)RmsSourceKind.Derived);
			clear.SourceSystem.Should().Be("UnitStates (incident command)");
			aggregate.Units.Single().TimesSourceKind.Should().Be((int)RmsSourceKind.Derived);
		}

		[Test]
		public async Task Command_objectives_prefill_the_tactic_timestamps_section()
		{
			RunUnderCommand(
				Completed("o1", "Primary search - all clear", LoggedOn.AddMinutes(18)),
				Completed("o2", "Fire under control", LoggedOn.AddMinutes(31)),
				Completed("o3", "Water supply established", LoggedOn.AddMinutes(9)));

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var module = aggregate.Modules.Should().ContainSingle(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps).Subject;
			module.SchemaName.Should().Be("IncidentTacticTimestampsPayload");
			var body = Body(module.DetailJson);
			((string)body[NerisTacticTimestamps.CommandEstablished]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(4)));
			((string)body[NerisTacticTimestamps.PrimarySearchComplete]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(18)));
			((string)body[NerisTacticTimestamps.FireUnderControl]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(31)));
			body.Properties().Should().HaveCount(3, "water supply is not a NERIS tactic timestamp");

			var fact = aggregate.Facts.Single(f => f.FactKey == NerisTacticTimestamps.FactKey(NerisTacticTimestamps.FireUnderControl));
			fact.SourceKind.Should().Be((int)RmsSourceKind.Derived);
			fact.SourceEntityType.Should().Be("TacticalObjective");
			_store.Modules.Should().Contain(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps);

			var payload = JObject.Parse(new NerisMappingService().BuildIncidentPayloadJson(IncidentReportsService.ToSnapshot(aggregate), _profile));
			payload["tactic_timestamps"]?["fire_under_control"].Should().NotBeNull("the section rides the incident payload");
		}

		[Test]
		public async Task Mutual_aid_command_tracked_arrives_as_a_received_aid_row_that_survives_a_save()
		{
			RunUnderCommand();
			_resources.Setup(r => r.GetAdHocUnitsForCallAsync(Dept, CallId, true)).ReturnsAsync(new List<IncidentAdHocUnit>
			{
				new IncidentAdHocUnit { IncidentAdHocUnitId = "a1", DepartmentId = Dept, CallId = CallId, Name = "Engine 41", ExternalAgencyName = "Lakeside FD", CreatedOn = LoggedOn.AddMinutes(20), ReleasedOn = LoggedOn.AddMinutes(70) }
			});

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var aid = aggregate.Aids.Should().ContainSingle().Subject;
			aid.CounterpartName.Should().Be("Lakeside FD", "released mutual aid still worked the incident");
			aid.Direction.Should().Be("RECEIVED");
			aid.AidType.Should().BeEmpty("the officer chooses the aid type");
			aggregate.Facts.Should().Contain(f => f.FactKey.StartsWith("aid.", StringComparison.Ordinal) && f.SourceValue.Contains("Engine 41"));

			var saved = await _service.SaveDraftAsync(Dept, "author", aggregate.Report.RmsIncidentReportId, aggregate.Report.RowVersion, DraftFrom(aggregate), true);
			saved.Aids.Should().ContainSingle(a => a.CounterpartName == "Lakeside FD", "a row naming only the agency waits for its aid type");
		}

		[Test]
		public async Task Incident_number_prefers_the_number_dispatch_entered()
		{
			_call.IncidentNumber = "F26-0042";

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			aggregate.Report.IncidentNumber.Should().Be("F26-0042");
			aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.IncidentNumber).SourceValue.Should().Be("F26-0042");
		}

		[Test]
		public async Task Refresh_fills_what_arrived_after_the_start_and_keeps_the_authors_corrections()
		{
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitStateId = 2, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10) }
			});
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Units.Single().ClearedOn.Should().BeNull("the unit is still on scene");

			var input = DraftFrom(started);
			input.Units[0].OnSceneOn = LoggedOn.AddMinutes(11);
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);

			// The incident ends: the unit clears, a second unit turns up, the call closes.
			_units.Setup(u => u.GetUnitByIdAsync(6)).ReturnsAsync(new Unit { UnitId = 6, DepartmentId = Dept, Name = "Truck 6", StationGroupId = 13 });
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitStateId = 2, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10) },
				new UnitState { UnitStateId = 3, UnitId = 6, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(25) },
				new UnitState { UnitStateId = 4, UnitId = 5, State = (int)UnitStateTypes.Available, Timestamp = LoggedOn.AddMinutes(60) }
			});
			_call.ClosedOn = LoggedOn.AddMinutes(65);

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			result.Changed.Should().BeTrue();
			var engine = result.Aggregate.Units.Single(u => u.UnitId == 5);
			engine.ClearedOn.Should().Be(LoggedOn.AddMinutes(60));
			engine.OnSceneOn.Should().Be(LoggedOn.AddMinutes(11), "the author's correction is kept");
			result.Aggregate.Units.Should().Contain(u => u.UnitId == 6 && u.OnSceneOn == LoggedOn.AddMinutes(25));
			result.Aggregate.Report.IncidentClearedOn.Should().Be(LoggedOn.AddMinutes(65));
			result.AddedCount.Should().BeGreaterThanOrEqualTo(1);
			result.FilledCount.Should().BeGreaterThanOrEqualTo(2);
			result.Aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "on_scene")).CorrectedOn.Should().NotBeNull();
		}

		[Test]
		public async Task Refresh_moves_an_uncorrected_time_with_its_source()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Units.Single().OnSceneOn.Should().Be(LoggedOn.AddMinutes(10));

			// A status replayed from the unit's offline queue turns out to have been earlier.
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 1, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitStateId = 7, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(8) },
				new UnitState { UnitStateId = 2, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10) },
				new UnitState { UnitStateId = 3, UnitId = 5, State = (int)UnitStateTypes.Available, Timestamp = LoggedOn.AddMinutes(60) }
			});

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion);

			result.Aggregate.Units.Single().OnSceneOn.Should().Be(LoggedOn.AddMinutes(8));
			result.UpdatedCount.Should().Be(1);
			var fact = result.Aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "on_scene"));
			fact.SourceValue.Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(8)));
			fact.CorrectedOn.Should().BeNull("the value followed its source; nobody corrected it");
		}

		[Test]
		public async Task Refresh_adds_tactic_timestamps_without_replacing_one_the_author_changed()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Modules.Should().BeEmpty("the call had no command when the report was started");

			RunUnderCommand(Completed("o1", "Knockdown", LoggedOn.AddMinutes(22)));
			var first = await _service.RefreshFromSourcesAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion);
			var module = first.Aggregate.Modules.Single(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps);
			((string)Body(module.DetailJson)[NerisTacticTimestamps.FireKnockedDown]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(22)));

			// The author moves knockdown on the form; then command adds another objective.
			var input = DraftFrom(first.Aggregate);
			input.Modules = new List<IncidentModuleInput>
			{
				new IncidentModuleInput { ModuleId = module.RmsIncidentModuleId, Kind = RmsIncidentModuleKind.TacticTimestamps, DetailJson = "{\"command_established\":\"" + IncidentReportsService.Iso(LoggedOn.AddMinutes(4)) + "\",\"fire_knocked_down\":\"" + IncidentReportsService.Iso(LoggedOn.AddMinutes(24)) + "\"}" }
			};
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, first.Aggregate.Report.RowVersion, input, true);
			RunUnderCommand(Completed("o1", "Knockdown", LoggedOn.AddMinutes(22)), Completed("o2", "Extrication complete", LoggedOn.AddMinutes(27)));

			var second = await _service.RefreshFromSourcesAsync(Dept, "author", started.Report.RmsIncidentReportId, saved.Report.RowVersion);

			var body = Body(second.Aggregate.Modules.Single(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps).DetailJson);
			((string)body[NerisTacticTimestamps.FireKnockedDown]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(24)), "the author's value is kept");
			((string)body[NerisTacticTimestamps.ExtricationComplete]).Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(27)));
		}

		[Test]
		public async Task Refresh_keeps_a_time_the_author_cleared()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Units.Single().ClearedOn.Should().Be(LoggedOn.AddMinutes(60));

			// The prefilled clear time is wrong and the author deletes it.
			var input = DraftFrom(started);
			input.Units[0].ClearedOn = null;
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			result.Aggregate.Units.Single().ClearedOn.Should().BeNull("a cleared value is the author's correction, not a gap to fill");
			var fact = result.Aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "unit_clear"));
			fact.CorrectedOn.Should().NotBeNull();
			fact.CurrentValue.Should().BeNull();
			fact.SourceValue.Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(60)), "the source value is kept for the record");
		}

		[Test]
		public async Task Refresh_keeps_staffing_the_author_cleared()
		{
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitStateId = 100, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) }
			});
			_crew.Setup(c => c.GetRolesForUnitStatesAsync(It.IsAny<IReadOnlyCollection<int>>())).ReturnsAsync(new List<UnitStateRole>
			{
				new UnitStateRole { UnitStateId = 100, UserId = "crew-a", Role = "Officer" },
				new UnitStateRole { UnitStateId = 100, UserId = "crew-b", Role = "Driver" }
			});
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Units.Single().Staffing.Should().Be(2);

			var input = DraftFrom(started);
			input.Units[0].Staffing = null;
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			result.Aggregate.Units.Single().Staffing.Should().BeNull();
			result.Aggregate.Facts.Single(f => f.FactKey == IncidentSourceFactKeys.UnitStaffing(5)).CorrectedOn.Should().NotBeNull();
		}

		[Test]
		public async Task Refresh_leaves_out_a_unit_the_author_removed()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Units.Should().ContainSingle(u => u.UnitId == 5);

			var input = DraftFrom(started);
			input.Units.Clear();
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);
			saved.Units.Should().BeEmpty();

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			result.Aggregate.Units.Should().BeEmpty("the author took the engine off the report");
			result.AddedCount.Should().Be(0);
		}

		[Test]
		public async Task Refresh_leaves_cleared_tactic_timestamps_cleared_and_adds_new_ones()
		{
			RunUnderCommand(Completed("o1", "Knockdown", LoggedOn.AddMinutes(22)));
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Modules.Should().ContainSingle(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps);

			// The author clears every time on the form, which posts no tactic timestamps section at all.
			var input = DraftFrom(started);
			input.Modules = new List<IncidentModuleInput>();
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);
			saved.Modules.Should().NotContain(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps);
			RunUnderCommand(Completed("o1", "Knockdown", LoggedOn.AddMinutes(22)), Completed("o2", "Extrication complete", LoggedOn.AddMinutes(27)));

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			var body = Body(result.Aggregate.Modules.Single(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps).DetailJson);
			body.Properties().Select(p => p.Name).Should().BeEquivalentTo(new[] { NerisTacticTimestamps.ExtricationComplete }, "only the time the author never saw arrives");
			result.Aggregate.Facts.Where(f => f.FactKey.StartsWith("tactic_timestamps.", StringComparison.Ordinal))
				.GroupBy(f => f.FactKey).Should().OnlyContain(g => g.Count() == 1, "a refresh never stores a second fact for a key");
		}

		[Test]
		public async Task Refresh_leaves_out_mutual_aid_the_author_removed()
		{
			RunUnderCommand();
			_resources.Setup(r => r.GetAdHocUnitsForCallAsync(Dept, CallId, true)).ReturnsAsync(new List<IncidentAdHocUnit>
			{
				new IncidentAdHocUnit { IncidentAdHocUnitId = "a1", DepartmentId = Dept, CallId = CallId, Name = "Engine 41", ExternalAgencyName = "Lakeside FD", CreatedOn = LoggedOn.AddMinutes(20) }
			});
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			started.Aids.Should().ContainSingle();

			var input = DraftFrom(started);
			input.Aids.Clear();
			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input, true);

			var result = await _service.RefreshFromSourcesAsync(Dept, "author", saved.Report.RmsIncidentReportId, saved.Report.RowVersion);

			result.Aggregate.Aids.Should().BeEmpty("the author removed the agency");
		}

		[Test]
		public async Task Refresh_on_a_finalized_report_is_refused()
		{
			_profile.AutoSubmitOnFinalize = false;
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var finalized = await _service.FinalizeAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, IncidentReportsService.AttestationStatementVersion, "127.0.0.1", null, null);

			Func<Task> refresh = () => _service.RefreshFromSourcesAsync(Dept, "author", finalized.Report.RmsIncidentReportId, finalized.Report.RowVersion);

			await refresh.Should().ThrowAsync<RecordTransitionException>();
		}
	}
}
