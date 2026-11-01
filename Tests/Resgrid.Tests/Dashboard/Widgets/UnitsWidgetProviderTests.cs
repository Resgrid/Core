using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Operations;
using static Resgrid.Tests.Dashboard.Widgets.OperationsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>units.status, units.summary, units.alerts, units.routes (plan §7.2).</summary>
	[TestFixture]
	public class UnitsWidgetProviderTests
	{
		private Unit _engine1;
		private Unit _engine2;
		private Unit _ladder;
		private Unit _hidden;
		private Unit _deleted;
		private Unit _foreign;
		private Mock<IUnitsService> _units;
		private Mock<ICustomStateService> _customStates;
		private Mock<IDepartmentGroupsService> _groups;
		private List<UnitState> _states;

		private const int Station1 = 10;
		private const int Station2 = 20;
		private const int CustomDispatched = 9001;

		[SetUp]
		public void SetUp()
		{
			_engine1 = Unit(1, "Engine 1", Station1);
			_engine2 = Unit(2, "Engine 2", Station2);
			_ladder = Unit(3, "Ladder 1", Station1, type: "Ladder");
			_hidden = Unit(4, "Hidden 1", Station1);
			_deleted = Unit(5, "Old Engine", Station1, deleted: true);
			_foreign = Unit(6, "Foreign", Station1, department: OtherDept);

			_states = new List<UnitState>
			{
				UnitState(_engine1, CustomDispatched, 101, Now.AddMinutes(-30), note: "rgdp:1:3:AAAA"),
				UnitState(_engine2, (int)UnitStateTypes.Available, 102, Now.AddMinutes(-90), note: "Fuel low"),
				new UnitState { UnitStateId = 0, UnitId = 3, Unit = _ladder, State = (int)UnitStateTypes.Available, Timestamp = Now },
				UnitState(_hidden, (int)UnitStateTypes.OutOfService, 104),
				UnitState(_deleted, (int)UnitStateTypes.OutOfService, 105),
				UnitState(_foreign, (int)UnitStateTypes.OutOfService, 106)
			};

			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(Dept)).ReturnsAsync(() => _states);
			_units.Setup(u => u.GetAllActiveRolesForUnitsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { DepartmentId = Dept, UnitId = 1, UserId = "a" },
				new UnitActiveRole { DepartmentId = Dept, UnitId = 1, UserId = "b" },
				new UnitActiveRole { DepartmentId = Dept, UnitId = 1, UserId = null }
			});
			_units.Setup(u => u.GetUnitTypesForDepartmentAsync(Dept)).ReturnsAsync(new List<UnitType>
			{
				new UnitType { UnitTypeId = 70, DepartmentId = Dept, Type = "Ladder" },
				new UnitType { UnitTypeId = 71, DepartmentId = OtherDept, Type = "Engine" }
			});

			_customStates = CustomStateService(CustomStates(CustomStateTypes.Unit,
				Detail(CustomDispatched, "Dispatched", "#AA0000", (int)ActionBaseTypes.Dispatched, "#FFFFFF")));

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetAllStationGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>
			{
				Group(Station1, "Station 1", type: (int)DepartmentGroupTypes.Station),
				Group(Station2, "Station 2", type: (int)DepartmentGroupTypes.Station)
			});
		}

		private UnitsStatusWidgetProvider StatusProvider(Mock<IProtectedReadService> protectedReads, Mock<ICallsService> calls = null) =>
			new UnitsStatusWidgetProvider(_units.Object, _customStates.Object, Matrix(hiddenUnits: new[] { 4 }).Object, _groups.Object,
				(calls ?? Calls()).Object, DepartmentWideScope().Object, protectedReads.Object);

		private static Mock<IProtectedReadService> RedactingReads(params string[] fieldIds)
		{
			var reads = new Mock<IProtectedReadService>(MockBehavior.Strict);
			reads.Setup(r => r.ResolveUnitStatesForReadAsync(Dept, It.IsAny<IReadOnlyList<UnitState>>(), It.IsAny<string>(), Viewer, It.IsAny<CancellationToken>()))
				.Returns((int d, IReadOnlyList<UnitState> states, string grant, string user, CancellationToken t) =>
				{
					foreach (var state in states.Where(s => ProtectedDataEnvelope.HasEnvelopePrefix(s.Note)))
						state.Note = grant == "good" ? "Crew at hospital" : ProtectedDataEnvelope.RedactionValue;

					var result = new ProtectedReadResult { IsProtected = true };
					if (grant != "good")
						result.RedactedFields.AddRange(fieldIds);
					return Task.FromResult(result);
				});
			return reads;
		}

		[Test]
		public async Task Status_lists_visible_active_units_with_department_colours_and_crew()
		{
			var data = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note"))
				.GetDataAsync(Context("units.status"), CancellationToken.None);

			data.TotalCount.Should().Be(3, "the matrix hides one unit, the deleted and foreign units are never listed");
			data.Groups.Select(g => g.Key).Should().Equal("station:10", "station:20");
			var engine1 = data.Groups[0].Units.Single(u => u.UnitId == 1);
			engine1.Status.Should().BeEquivalentTo(new DashboardStatusChip
			{
				Id = CustomDispatched, Text = "Dispatched", Color = "#AA0000", TextColor = "#FFFFFF",
				BaseType = (int)ActionBaseTypes.Dispatched, Availability = (int)AvailabilityClass.Committed, IsCustom = true
			});
			engine1.CrewCount.Should().Be(2);
			engine1.StatusOnUtc.Should().Be(Now.AddMinutes(-30));
			engine1.Url.Should().Be("/User/Units/ViewEvents?unitId=1");

			var engine2 = data.Groups[1].Units.Single();
			engine2.Status.Color.Should().Be("#d1dade", "the built-in Available keeps its default colour");
			engine2.Status.IsCustom.Should().BeFalse();

			var ladder = data.Groups[0].Units.Single(u => u.UnitId == 3);
			ladder.HasStatus.Should().BeFalse();
			ladder.StatusOnUtc.Should().BeNull();
		}

		[Test]
		public async Task Status_note_is_a_shield_without_a_grant_and_text_with_one()
		{
			var viewer = ViewerWith("Unit_View");
			viewer.AdpEnforced = true;
			viewer.AdpViewPermissions.Add(PermissionTypes.ViewProtectedOperationalData);

			var hidden = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note")).GetDataAsync(Context("units.status", viewer), CancellationToken.None);
			var engine1 = hidden.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 1);
			engine1.Note.IsProtected.Should().BeTrue();
			engine1.Note.CanReveal.Should().BeTrue();
			engine1.Note.Value.Should().BeNull("neither the placeholder nor the envelope reaches the client");
			hidden.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 2).Note.Should().BeEquivalentTo(DashboardProtectedText.Plain("Fuel low"));
			hidden.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 3).Note.Should().BeNull();

			// The read service resolves the request's own instances in place; the next request reads the stored envelope again.
			_states[0].Note = "rgdp:1:3:AAAA";
			var revealed = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note")).GetDataAsync(Context("units.status", viewer, grant: "good"), CancellationToken.None);
			revealed.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 1).Note.Should().BeEquivalentTo(DashboardProtectedText.Plain("Crew at hospital"));
		}

		[Test]
		public async Task Status_shield_says_when_the_viewers_role_cannot_reveal()
		{
			var viewer = ViewerWith("Unit_View");
			viewer.AdpEnforced = true;

			var data = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note")).GetDataAsync(Context("units.status", viewer), CancellationToken.None);

			var note = data.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 1).Note;
			note.IsProtected.Should().BeTrue();
			note.CanReveal.Should().BeFalse();
		}

		[Test]
		public async Task Status_filters_recheck_department_ids()
		{
			var foreignStation = (UnitsStatusWidgetData)await StatusProvider(RedactingReads())
				.GetDataAsync(Context("units.status", settings: new Dictionary<string, object> { ["station"] = "777" }), CancellationToken.None);
			foreignStation.StationFilterIgnored.Should().BeTrue();
			foreignStation.TotalCount.Should().Be(3);

			var station2 = (UnitsStatusWidgetData)await StatusProvider(RedactingReads())
				.GetDataAsync(Context("units.status", settings: new Dictionary<string, object> { ["station"] = "20" }), CancellationToken.None);
			station2.Groups.SelectMany(g => g.Units).Select(u => u.UnitId).Should().Equal(2);

			var types = (UnitsStatusWidgetData)await StatusProvider(RedactingReads())
				.GetDataAsync(Context("units.status", settings: new Dictionary<string, object> { ["unitTypes"] = new[] { "70", "71" }, ["groupBy"] = "type" }), CancellationToken.None);
			types.Groups.Select(g => g.Key).Should().Equal("type:Ladder");
			types.Groups.Single().Units.Select(u => u.UnitId).Should().Equal(new[] { 3 }, "another department's unit type id is ignored");
		}

		[Test]
		public async Task Status_hide_available_keeps_committed_units_only()
		{
			var data = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note"))
				.GetDataAsync(Context("units.status", settings: new Dictionary<string, object> { ["hideAvailable"] = true, ["groupBy"] = "none" }), CancellationToken.None);

			data.TotalCount.Should().Be(3);
			data.ShownCount.Should().Be(1);
			data.Groups.Single().Key.Should().Be("all");
			data.Groups.Single().Units.Select(u => u.UnitId).Should().Equal(1);
		}

		[Test]
		public async Task Status_names_only_destinations_in_the_viewers_scope()
		{
			_states[0].DestinationId = 55;
			_states[0].DestinationType = (int)DestinationEntityTypes.Call;
			_states[1].DestinationId = Station1;
			_states[1].DestinationType = (int)DestinationEntityTypes.Station;
			var calls = Calls(new List<Call> { Call(55, name: "Structure fire") });

			var scoped = new UnitsStatusWidgetProvider(_units.Object, _customStates.Object, Matrix(hiddenUnits: new[] { 4 }).Object, _groups.Object,
				calls.Object, ScopedTo().Object, RedactingReads("unitstates.note").Object);
			var data = (UnitsStatusWidgetData)await scoped.GetDataAsync(Context("units.status"), CancellationToken.None);

			var rows = data.Groups.SelectMany(g => g.Units).ToList();
			rows.Single(u => u.UnitId == 1).DestinationName.Should().BeNull("the call is outside the viewer's dispatch scope");
			rows.Single(u => u.UnitId == 2).DestinationName.Should().Be("Station 1");

			var wide = (UnitsStatusWidgetData)await StatusProvider(RedactingReads("unitstates.note"), calls).GetDataAsync(Context("units.status"), CancellationToken.None);
			wide.Groups.SelectMany(g => g.Units).Single(u => u.UnitId == 1).DestinationName.Should().Be("C-55: Structure fire");
		}

		[Test]
		public async Task Status_reads_no_protected_values_when_no_unit_has_a_note()
		{
			foreach (var state in _states)
				state.Note = null;

			// Strict, with nothing set up: any protected read would throw.
			var data = (UnitsStatusWidgetData)await StatusProvider(new Mock<IProtectedReadService>(MockBehavior.Strict))
				.GetDataAsync(Context("units.status"), CancellationToken.None);

			data.ShownCount.Should().Be(3);
		}

		[Test]
		public async Task Summary_counts_visible_units_by_meaning()
		{
			var provider = new UnitsSummaryWidgetProvider(_units.Object, _customStates.Object, Matrix(hiddenUnits: new[] { 4 }).Object);
			var data = (UnitsSummaryWidgetData)await provider.GetDataAsync(Context("units.summary"), CancellationToken.None);

			data.Total.Should().Be(3);
			data.Available.Should().Be(2);
			data.Committed.Should().Be(1);
			data.Unavailable.Should().Be(0, "the out-of-service units are hidden, deleted or another department's");
			data.InService.Should().Be(3);
			data.ByStatus.First().Count.Should().Be(2);
		}

		[Test]
		public async Task Summary_of_an_empty_department_is_zero()
		{
			_states.Clear();
			var provider = new UnitsSummaryWidgetProvider(_units.Object, new Mock<ICustomStateService>(MockBehavior.Strict).Object, Matrix().Object);

			var data = (UnitsSummaryWidgetData)await provider.GetDataAsync(Context("units.summary"), CancellationToken.None);

			data.Total.Should().Be(0);
			data.ByStatus.Should().BeEmpty();
		}

		#region units.alerts

		private UnitsAlertsWidgetProvider AlertsProvider(UnitStatusThresholds thresholds, List<UnitStatusAlertAcknowledgement> acknowledgements, Mock<IUnitsService> units = null)
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetUnitStatusThresholdsAsync(Dept, It.IsAny<bool>())).ReturnsAsync(thresholds);
			var alerts = new Mock<IUnitStatusAlertsService>();
			alerts.Setup(a => a.GetCurrentAcknowledgementsForDepartmentAsync(Dept)).ReturnsAsync(acknowledgements);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "dispatcher-1", FirstName = "Dana", LastName = "Disp" }
			});

			return new UnitsAlertsWidgetProvider((units ?? _units).Object, _customStates.Object, Matrix(hiddenUnits: new[] { 4 }).Object, settings.Object,
				alerts.Object, departments.Object, _groups.Object);
		}

		private static UnitStatusThresholds Timers() => new UnitStatusThresholds
		{
			AcknowledgementNoteRequirement = UnitStatusAlertNoteRequirements.AtAlert,
			Thresholds = new List<UnitStatusThreshold>
			{
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 600, AlertSeconds = 1200 },
				new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Unavailable, WarnSeconds = 60, AlertSeconds = 120 }
			}
		};

		[Test]
		public async Task Alerts_without_timers_read_no_units()
		{
			var data = (UnitsAlertsWidgetData)await AlertsProvider(new UnitStatusThresholds(), new List<UnitStatusAlertAcknowledgement>(), new Mock<IUnitsService>(MockBehavior.Strict))
				.GetDataAsync(Context("units.alerts"), CancellationToken.None);

			data.TimersConfigured.Should().BeFalse();
			data.Alerts.Should().BeEmpty();
		}

		[Test]
		public async Task Alerts_time_visible_units_and_match_the_current_episodes_acknowledgement()
		{
			var data = (UnitsAlertsWidgetData)await AlertsProvider(Timers(), new List<UnitStatusAlertAcknowledgement>())
				.GetDataAsync(Context("units.alerts"), CancellationToken.None);

			data.TimersConfigured.Should().BeTrue();
			data.Alerts.Select(a => a.UnitId).Should().Equal(new[] { 1 }, "the hidden out-of-service unit is not timed for this viewer");
			var engine = data.Alerts.Single();
			engine.Level.Should().Be((int)UnitStatusAlertLevels.Alert);
			engine.NoteRequired.Should().BeTrue("the department requires a note at alert level");
			engine.Acknowledgement.Should().BeNull();
			data.CanAcknowledge.Should().BeFalse("acknowledging is for Call_Create holders");
			data.Acknowledge.Available.Should().BeTrue("the web BFF forwards api/v4/UnitStatusAlerts/");
			data.Acknowledge.Url.Should().Be("/api/web-bff/api/v4/UnitStatusAlerts/Acknowledge");

			var acknowledged = (UnitsAlertsWidgetData)await AlertsProvider(Timers(), new List<UnitStatusAlertAcknowledgement>
				{
					new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "old", DepartmentId = Dept, UnitId = 1, UnitStateId = 99, Level = 2, AcknowledgedOn = Now.AddMinutes(-40) },
					new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "ack", DepartmentId = Dept, UnitId = 1, UnitStateId = 101, Level = 2, Mode = 1,
						Note = "On a long job", AcknowledgedByUserId = "DISPATCHER-1", AcknowledgedOn = Now.AddMinutes(-2), MutedUntil = Now.AddMinutes(30) }
				})
				.GetDataAsync(Context("units.alerts", ViewerWith("Unit_View", "Call_Create"), new Dictionary<string, object> { ["showMuted"] = false }), CancellationToken.None);

			acknowledged.MutedCount.Should().Be(1);
			acknowledged.Alerts.Should().BeEmpty("muted alerts are collapsed when the setting hides them");
			acknowledged.CanAcknowledge.Should().BeTrue();

			var shown = (UnitsAlertsWidgetData)await AlertsProvider(Timers(), new List<UnitStatusAlertAcknowledgement>
				{
					new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "ack", DepartmentId = Dept, UnitId = 1, UnitStateId = 101, Level = 2, Mode = 1,
						Note = "On a long job", AcknowledgedByUserId = "dispatcher-1", AcknowledgedOn = Now.AddMinutes(-2), MutedUntil = Now.AddMinutes(30) }
				})
				.GetDataAsync(Context("units.alerts"), CancellationToken.None);
			shown.Alerts.Single().Acknowledgement.Mode.Should().Be((int)UnitStatusAlertAcknowledgementModes.Muted);
			shown.Alerts.Single().Acknowledgement.AcknowledgedByName.Should().Be("Dana Disp");
		}

		#endregion units.alerts

		#region units.routes

		[Test]
		public async Task Routes_list_active_instances_and_open_deviations_with_names()
		{
			var routes = new Mock<IRouteService>();
			routes.Setup(r => r.GetActiveInstancesForDepartmentAsync(Dept)).ReturnsAsync(new List<RouteInstance>
			{
				new RouteInstance { RouteInstanceId = "ri-1", RoutePlanId = "rp-1", UnitId = 1, DepartmentId = Dept, Status = (int)RouteInstanceStatus.InProgress, ActualStartOn = Now.AddHours(-1), StopsCompleted = 2, StopsTotal = 5 },
				new RouteInstance { RouteInstanceId = "ri-x", RoutePlanId = "rp-1", UnitId = 1, DepartmentId = OtherDept, Status = (int)RouteInstanceStatus.InProgress }
			});
			routes.Setup(r => r.GetUnacknowledgedDeviationsAsync(Dept)).ReturnsAsync(new List<RouteDeviation>
			{
				new RouteDeviation { RouteDeviationId = "d-1", RouteInstanceId = "ri-1", DetectedOn = Now.AddMinutes(-3), DeviationDistanceMeters = 120 },
				new RouteDeviation { RouteDeviationId = "d-2", RouteInstanceId = "ri-old", DetectedOn = Now.AddMinutes(-30) }
			});
			routes.Setup(r => r.GetRoutePlansForDepartmentAsync(Dept)).ReturnsAsync(new List<RoutePlan>
			{
				new RoutePlan { RoutePlanId = "rp-1", DepartmentId = Dept, Name = "Hydrant loop", RouteColor = "#3388ff" }
			});
			_units.Setup(u => u.GetUnitsForDepartmentAsync(Dept)).ReturnsAsync(new List<Unit> { _engine1 });

			var data = (UnitsRoutesWidgetData)await new UnitsRoutesWidgetProvider(routes.Object, _units.Object).GetDataAsync(Context("units.routes"), CancellationToken.None);

			data.ActiveRouteCount.Should().Be(1, "another department's instance is never listed");
			data.ActiveRoutes.Single().Should().BeEquivalentTo(new
			{
				PlanName = "Hydrant loop", PlanColor = "#3388ff", UnitName = "Engine 1", DeviationCount = 1, Url = "/User/Routes/InstanceDetail?instanceId=ri-1"
			}, o => o.ExcludingMissingMembers());
			data.UnacknowledgedDeviationCount.Should().Be(2);
			data.Deviations.Select(d => d.RouteDeviationId).Should().Equal("d-1", "d-2");
			data.Deviations[1].PlanName.Should().BeNull("its route is no longer active");
			data.AcknowledgeDeviation.Available.Should().BeTrue();
		}

		[Test]
		public async Task Routes_with_nothing_running_read_no_plans()
		{
			var routes = new Mock<IRouteService>(MockBehavior.Strict);
			routes.Setup(r => r.GetActiveInstancesForDepartmentAsync(Dept)).ReturnsAsync(new List<RouteInstance>());
			routes.Setup(r => r.GetUnacknowledgedDeviationsAsync(Dept)).ReturnsAsync(new List<RouteDeviation>());

			var data = (UnitsRoutesWidgetData)await new UnitsRoutesWidgetProvider(routes.Object, new Mock<IUnitsService>(MockBehavior.Strict).Object)
				.GetDataAsync(Context("units.routes"), CancellationToken.None);

			data.ActiveRoutes.Should().BeEmpty();
			data.Deviations.Should().BeEmpty();
		}

		#endregion units.routes
	}
}
