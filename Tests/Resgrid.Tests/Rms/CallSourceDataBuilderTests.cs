using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The report-source rules: unit times agree with the Call Unit Times report, crews come from seats and riders, each
	/// status says who set it and from where, command entries and objectives join the timeline, protected text never
	/// leaves, and NERIS tactic timestamps are read only from objectives whose names say so.
	/// </summary>
	[TestFixture]
	public class CallSourceDataBuilderTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);

		private static CallSourceInputs Inputs()
		{
			return new CallSourceInputs
			{
				Call = new Call
				{
					CallId = 9, DepartmentId = 1, Number = "26-9", LoggedOn = T0, ReportingUserId = "disp",
					UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallDispatchUnitId = 1, UnitId = 5, DispatchedOn = T0.AddMinutes(1) } },
					Dispatches = new List<CallDispatch> { new CallDispatch { CallDispatchId = 1, UserId = "paged", DispatchedOn = T0.AddMinutes(1) } }
				},
				Units = new Dictionary<int, Unit> { [5] = new Unit { UnitId = 5, Name = "Engine 5" }, [6] = new Unit { UnitId = 6, Name = "Truck 6" } },
				Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["disp"] = "Dee Spatch", ["ann"] = "Ann Able", ["ben"] = "Ben Baker", ["paged"] = "Pat Paged" },
				Now = T0.AddHours(2)
			};
		}

		[Test]
		public void Unit_times_match_the_call_unit_times_report_and_track_their_status_entries()
		{
			var input = Inputs();
			input.UnitStates = new List<UnitState>
			{
				new UnitState { UnitStateId = 10, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = T0.AddMinutes(2), SetByUserId = "ann", SetByOrigin = (int)StatusSetOrigins.UnitApp },
				new UnitState { UnitStateId = 11, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = T0.AddMinutes(9), SetByUserId = "disp", SetByOrigin = (int)StatusSetOrigins.Web },
				new UnitState { UnitStateId = 12, UnitId = 5, State = (int)UnitStateTypes.Returning, Timestamp = T0.AddMinutes(40) },
				new UnitState { UnitStateId = 13, UnitId = 5, State = (int)UnitStateTypes.Available, Timestamp = T0.AddMinutes(55) }
			};

			var data = CallSourceDataBuilder.Build(input);

			var expected = CallUnitTimesCalculator.Compute(input.Call.UnitDispatches, input.UnitStates, input.UnitBaseTypes).Single();
			var unit = data.Units.Single();
			unit.DispatchedOn.Should().Be(expected.DispatchedOn);
			unit.EnrouteOn.Should().Be(expected.EnrouteOn);
			unit.OnSceneOn.Should().Be(expected.OnSceneOn);
			unit.ClearedOn.Should().Be(T0.AddMinutes(40)).And.Be(expected.ClearedOn);
			unit.InServiceOn.Should().Be(T0.AddMinutes(55));
			unit.CancelledOn.Should().BeNull("it arrived");

			unit.TimeEntries[CallSourceMilestone.OnScene].SetByName.Should().Be("Dee Spatch");
			unit.TimeEntries[CallSourceMilestone.OnScene].Origin.Should().Be((int)StatusSetOrigins.Web);
			unit.TimeEntries[CallSourceMilestone.Dispatched].Kind.Should().Be(CallSourceEntryKind.UnitDispatch);
			data.Entries.Should().Contain(e => e.Kind == CallSourceEntryKind.Call && e.Milestone == CallSourceMilestone.CallCreated && e.SetByName == "Dee Spatch");
			data.Entries.Select(e => e.TimestampUtc).Should().BeInAscendingOrder();
		}

		[Test]
		public void A_unit_cancelled_before_arriving_carries_a_cancelled_time()
		{
			var input = Inputs();
			input.UnitStates = new List<UnitState>
			{
				new UnitState { UnitStateId = 10, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = T0.AddMinutes(2) },
				new UnitState { UnitStateId = 11, UnitId = 5, State = (int)UnitStateTypes.Cancelled, Timestamp = T0.AddMinutes(4) }
			};

			var unit = CallSourceDataBuilder.Build(input).Units.Single();

			unit.CancelledOn.Should().Be(T0.AddMinutes(4));
			unit.OnSceneOn.Should().BeNull();
		}

		[Test]
		public void Crew_comes_from_seats_and_riders_and_staffing_counts_each_member_once()
		{
			var input = Inputs();
			input.UnitStates = new List<UnitState> { new UnitState { UnitStateId = 10, UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = T0.AddMinutes(2) } };
			input.Crew = new List<UnitStateRole>
			{
				new UnitStateRole { UnitStateId = 10, UserId = "ann", Role = "Officer" },
				new UnitStateRole { UnitStateId = 10, UserId = "ANN", Role = "Officer" }
			};
			input.ActionLogs = new List<ActionLog> { new ActionLog { ActionLogId = 3, UserId = "ben", ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 10, Timestamp = T0.AddMinutes(2) } };

			var data = CallSourceDataBuilder.Build(input);

			var unit = data.Units.Single();
			unit.Staffing.Should().Be(2);
			unit.CrewNames.Should().BeEquivalentTo(new[] { "Ann Able", "Ben Baker" });
			var ann = data.Personnel.Single(p => p.UserId == "ann");
			ann.UnitId.Should().Be(5);
			ann.Role.Should().Be("Officer");
			ann.Engaged.Should().BeTrue();
			data.Personnel.Single(p => p.UserId == "ben").UnitName.Should().Be("Engine 5");
		}

		[Test]
		public void Paged_personnel_who_never_turned_out_are_listed_but_not_engaged()
		{
			var input = Inputs();
			input.ActionLogs = new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, UserId = "ann", ActionTypeId = (int)ActionTypes.RespondingToScene, Timestamp = T0.AddMinutes(3), SetByUserId = "ann", SetByOrigin = (int)StatusSetOrigins.ResponderApp },
				new ActionLog { ActionLogId = 2, UserId = "ann", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0.AddMinutes(12), SetByUserId = "disp", SetByOrigin = (int)StatusSetOrigins.DispatchApp },
				new ActionLog { ActionLogId = 3, UserId = "ann", ActionTypeId = (int)ActionTypes.StandingBy, Timestamp = T0.AddMinutes(50) }
			};

			var data = CallSourceDataBuilder.Build(input);

			var ann = data.Personnel.Single(p => p.UserId == "ann");
			ann.RespondingOn.Should().Be(T0.AddMinutes(3));
			ann.OnSceneOn.Should().Be(T0.AddMinutes(12));
			ann.ClearedOn.Should().Be(T0.AddMinutes(50));
			data.Personnel.Single(p => p.UserId == "paged").Engaged.Should().BeFalse();
			data.Personnel.First().UserId.Should().Be("ann", "engaged members are listed first");

			var onScene = data.Entries.Single(e => e.Id == "personnel-status:2");
			onScene.SetByName.Should().Be("Dee Spatch", "a dispatcher set the member's status");
			onScene.Origin.Should().Be((int)StatusSetOrigins.DispatchApp);
		}

		[Test]
		public void Sealed_or_redacted_text_never_reaches_the_entries()
		{
			var input = Inputs();
			input.Call.NatureOfCall = ProtectedDataEnvelope.RedactionValue;
			input.UnitStates = new List<UnitState> { new UnitState { UnitStateId = 10, UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = T0.AddMinutes(9), Note = "rgdp:v1:sealed" } };

			var data = CallSourceDataBuilder.Build(input);

			data.Nature.Should().BeNull();
			data.Entries.Single(e => e.Kind == CallSourceEntryKind.UnitStatus).Detail.Should().BeNull();
		}

		[Test]
		public void Command_entries_objectives_tactic_timestamps_and_mutual_aid_come_from_incident_command()
		{
			var input = Inputs();
			input.Command = new IncidentCommand { IncidentCommandId = "c1", Name = "Main St", EstablishedOn = T0.AddMinutes(6), EstablishedByUserId = "ann", CurrentCommanderUserId = "ben" };
			input.FirstCommandEstablishedOn = T0.AddMinutes(5);
			input.CommandTimeline = new List<CommandLogEntry>
			{
				new CommandLogEntry { CommandLogEntryId = "l1", EntryType = (int)CommandLogEntryType.CommandEstablished, OccurredOn = T0.AddMinutes(5), UserId = "ann", Description = "Command established" },
				new CommandLogEntry { CommandLogEntryId = "l2", EntryType = (int)CommandLogEntryType.MapViewUpdated, OccurredOn = T0.AddMinutes(7), UserId = "ann" },
				new CommandLogEntry { CommandLogEntryId = "l3", EntryType = (int)CommandLogEntryType.ParCritical, OccurredOn = T0.AddMinutes(30), UserId = "ben" }
			};
			input.Objectives = new List<TacticalObjective>
			{
				new TacticalObjective { TacticalObjectiveId = "o1", Name = "360 complete", Status = (int)TacticalObjectiveStatus.Complete, CompletedOn = T0.AddMinutes(8), CompletedByUserId = "ann" },
				new TacticalObjective { TacticalObjectiveId = "o2", Name = "Secondary search complete", Status = (int)TacticalObjectiveStatus.Complete, CompletedOn = T0.AddMinutes(40) },
				new TacticalObjective { TacticalObjectiveId = "o3", Name = "Fire under control", Status = (int)TacticalObjectiveStatus.Pending }
			};
			input.AdHocUnits = new List<IncidentAdHocUnit> { new IncidentAdHocUnit { Name = "Engine 41", ExternalAgencyName = "Lakeside FD", CreatedOn = T0.AddMinutes(20) } };
			input.AdHocPersonnel = new List<IncidentAdHocPersonnel> { new IncidentAdHocPersonnel { Name = "Chief Lake", ExternalAgencyName = "lakeside fd", CreatedOn = T0.AddMinutes(25) } };
			input.Assignments = new List<ResourceAssignment>
			{
				new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.RealUnit, ResourceId = "6", AssignedOn = T0.AddMinutes(15), ReleasedOn = T0.AddMinutes(45) },
				new ResourceAssignment { ResourceKind = (int)ResourceAssignmentKind.LinkedDeptUnit, ResourceId = "900", AssignedOn = T0.AddMinutes(18) }
			};
			input.LinkedResourceDepartments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["900"] = 77 };
			input.LinkedResourceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["900"] = "Ladder 9" };
			input.DepartmentNames = new Dictionary<int, string> { [77] = "Hillside FD" };

			var data = CallSourceDataBuilder.Build(input);

			data.Command.EstablishedOn.Should().Be(T0.AddMinutes(5), "the earliest establishment across the call's commands");
			data.Command.CommanderNames.Should().Equal("Ann Able", "Ben Baker");
			data.Command.TacticTimestamps.Should().BeEquivalentTo(new Dictionary<string, DateTime>
			{
				[NerisTacticTimestamps.CommandEstablished] = T0.AddMinutes(5),
				[NerisTacticTimestamps.CompletedSizeup] = T0.AddMinutes(8)
			});
			data.Command.MutualAid.Select(a => a.AgencyName).Should().Equal("Hillside FD", "Lakeside FD");
			data.Command.MutualAid.Single(a => a.AgencyName == "Lakeside FD").ResourceNames.Should().Equal("Engine 41", "Chief Lake");
			data.Command.MutualAid.Single(a => a.AgencyName == "Hillside FD").LinkedDepartmentId.Should().Be(77);

			data.Entries.Should().NotContain(e => e.Id == "command:l2", "map edits are not report material");
			var par = data.Entries.Single(e => e.Id == "command:l3");
			par.SubjectName.Should().Be("Ben Baker", "a PAR entry is about the member");
			par.SetByUserId.Should().BeNull();
			data.Entries.Single(e => e.Id == "objective:o1").TacticTimestamp.Should().Be(NerisTacticTimestamps.CompletedSizeup);
			data.Entries.Single(e => e.Id == "objective:o2").TacticTimestamp.Should().BeNull();

			var truck = data.Units.Single(u => u.UnitId == 6);
			truck.AssignedByCommand.Should().BeTrue();
			truck.WasDispatched.Should().BeFalse();
			truck.CommandAssignedOn.Should().Be(T0.AddMinutes(15));
			truck.CommandReleasedOn.Should().Be(T0.AddMinutes(45));
		}
	}

	[TestFixture]
	public class NerisTacticTimestampsTests
	{
		[TestCase("Primary search all clear", NerisTacticTimestamps.PrimarySearchComplete)]
		[TestCase("PRIMARY SEARCH - COMPLETE", NerisTacticTimestamps.PrimarySearchComplete)]
		[TestCase("Primary search started", NerisTacticTimestamps.PrimarySearchBegin)]
		[TestCase("Secondary search all clear", null)]
		[TestCase("Fire under control", NerisTacticTimestamps.FireUnderControl)]
		[TestCase("Fire not under control", null)]
		[TestCase("Knockdown", NerisTacticTimestamps.FireKnockedDown)]
		[TestCase("Fire knocked down", NerisTacticTimestamps.FireKnockedDown)]
		[TestCase("Water on fire", NerisTacticTimestamps.WaterOnFire)]
		[TestCase("Water supply established", null)]
		[TestCase("360 complete", NerisTacticTimestamps.CompletedSizeup)]
		[TestCase("Size-up completed", NerisTacticTimestamps.CompletedSizeup)]
		[TestCase("Overhaul complete", NerisTacticTimestamps.SuppressionComplete)]
		[TestCase("Extrication complete", NerisTacticTimestamps.ExtricationComplete)]
		[TestCase("Utilities secured", null)]
		[TestCase("", null)]
		[TestCase(null, null)]
		public void Objective_names_map_only_when_they_say_which_milestone(string name, string expected)
		{
			NerisTacticTimestamps.MatchObjective(name).Should().Be(expected);
		}

		[Test]
		public void The_earliest_completion_wins_and_command_established_comes_from_the_command()
		{
			var t = new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);
			var result = NerisTacticTimestamps.FromCommand(t, new[] { ("Fire under control", (DateTime?)t.AddMinutes(30)), ("Under control", t.AddMinutes(20)), ("Knockdown", (DateTime?)null) });

			result.Should().BeEquivalentTo(new Dictionary<string, DateTime> { [NerisTacticTimestamps.CommandEstablished] = t, [NerisTacticTimestamps.FireUnderControl] = t.AddMinutes(20) });
		}
	}

	[TestFixture]
	public class StatusWriteActorTests
	{
		[Test]
		public void A_scope_stamps_unstamped_rows_and_restores_the_enclosing_scope()
		{
			var state = new UnitState();
			var log = new ActionLog { SetByUserId = "kept", SetByOrigin = (int)StatusSetOrigins.Sms };

			using (StatusWriteActor.Begin("dispatcher", StatusSetOrigins.DispatchApp))
			{
				StatusWriteActor.Stamp(state);
				StatusWriteActor.Stamp(log);
				using (StatusWriteActor.BeginAutomation(StatusSetOrigins.DispatchAutomation))
				{
					StatusWriteActor.UserId.Should().Be("dispatcher", "the member who dispatched stays the actor");
					StatusWriteActor.Origin.Should().Be(StatusSetOrigins.DispatchAutomation);
				}
				StatusWriteActor.Origin.Should().Be(StatusSetOrigins.DispatchApp);
			}

			StatusWriteActor.Origin.Should().Be(StatusSetOrigins.Unknown);
			state.SetByUserId.Should().Be("dispatcher");
			state.SetByOrigin.Should().Be((int)StatusSetOrigins.DispatchApp);
			log.SetByUserId.Should().Be("kept", "a caller-stamped row keeps its own actor");
			log.SetByOrigin.Should().Be((int)StatusSetOrigins.Sms);
		}

		[Test]
		public void Outside_any_scope_nothing_is_guessed()
		{
			var state = new UnitState();
			StatusWriteActor.Stamp(state);
			state.SetByOrigin.Should().BeNull();
			state.SetByUserId.Should().BeNull();
		}

		[TestCase(UserSessionClientApplication.Unit, StatusSetOrigins.UnitApp)]
		[TestCase(UserSessionClientApplication.Command, StatusSetOrigins.CommandApp)]
		[TestCase(UserSessionClientApplication.Dispatch, StatusSetOrigins.DispatchApp)]
		[TestCase(UserSessionClientApplication.UnknownLegacy, StatusSetOrigins.Api)]
		public void The_session_client_application_maps_to_an_origin(UserSessionClientApplication application, StatusSetOrigins expected)
		{
			StatusSetOriginsExtensions.FromClientApplication(application).Should().Be(expected);
		}
	}
}
