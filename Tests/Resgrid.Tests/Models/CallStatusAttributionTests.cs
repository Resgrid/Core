using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallStatusAttributionTests
	{
		private const int CallId = 42;
		private static readonly DateTime T0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

		private static bool Clearing(UnitState s) => CallStatusLinkage.IsClearingUnitState(s.State, null);

		private static UnitState State(int id, int minutes, UnitStateTypes state, int? destinationId = null, int? destinationType = null) =>
			new UnitState { UnitStateId = id, UnitId = 5, State = (int)state, Timestamp = T0.AddMinutes(minutes), DestinationId = destinationId, DestinationType = destinationType };

		[Test]
		public void carry_forward_needs_a_linked_open_call_and_a_non_clearing_previous_status()
		{
			CallStatusAttribution.CanCarryForward(CallId, false, true).Should().BeTrue();
			CallStatusAttribution.CanCarryForward(CallId, true, true).Should().BeFalse("the previous status cleared the call");
			CallStatusAttribution.CanCarryForward(CallId, false, false).Should().BeFalse("the call is closed");
			CallStatusAttribution.CanCarryForward(null, false, true).Should().BeFalse();
		}

		[Test]
		public void dispatch_rule_needs_exactly_one_candidate_other_than_the_cleared_call()
		{
			CallStatusAttribution.PickDispatchCall(new[] { 7 }, null).Should().Be(7);
			CallStatusAttribution.PickDispatchCall(new[] { 7, 7 }, null).Should().Be(7);
			CallStatusAttribution.PickDispatchCall(new[] { 7, 8 }, null).Should().BeNull("ambiguous");
			CallStatusAttribution.PickDispatchCall(new[] { 7, 8 }, 8).Should().Be(7);
			CallStatusAttribution.PickDispatchCall(new[] { 7 }, 7).Should().BeNull("the unit already cleared that call");
			CallStatusAttribution.PickDispatchCall(Array.Empty<int>(), null).Should().BeNull();
			CallStatusAttribution.PickDispatchCall(null, null).Should().BeNull();
		}

		[Test]
		public void inference_takes_unlinked_statuses_from_dispatch_until_the_first_clearing_status()
		{
			var states = new List<UnitState>
			{
				State(1, -5, UnitStateTypes.Available),                                          // before dispatch
				State(2, 1, UnitStateTypes.Responding, CallId, (int)DestinationEntityTypes.Call), // already linked
				State(3, 9, UnitStateTypes.OnScene),                                              // inferred
				State(4, 30, UnitStateTypes.Staging),                                             // inferred
				State(5, 50, UnitStateTypes.Available),                                           // inferred, clearing: stop
				State(6, 70, UnitStateTypes.Responding)                                           // after clearing
			};

			var inferred = CallStatusAttribution.InferUnitStates(CallId, T0, T0.AddHours(3), states, Clearing);

			inferred.Select(x => x.UnitStateId).Should().Equal(3, 4, 5);
			inferred.Should().OnlyContain(x => x.DestinationId == CallId && x.DestinationType == (int)DestinationEntityTypes.Call
				&& x.DestinationSource == (int)StatusDestinationSources.Inferred);
			states.Single(x => x.UnitStateId == 3).DestinationId.Should().BeNull("inferred rows are copies");
		}

		[Test]
		public void inference_stops_when_the_unit_moves_to_another_call_or_a_station()
		{
			var toAnotherCall = new List<UnitState>
			{
				State(1, 2, UnitStateTypes.Responding),
				State(2, 10, UnitStateTypes.Responding, 99, (int)DestinationEntityTypes.Call),
				State(3, 20, UnitStateTypes.OnScene)
			};
			CallStatusAttribution.InferUnitStates(CallId, T0, T0.AddHours(1), toAnotherCall, Clearing).Select(x => x.UnitStateId).Should().Equal(1);

			var toStation = new List<UnitState>
			{
				State(1, 2, UnitStateTypes.OnScene),
				State(2, 10, UnitStateTypes.Returning, 3, (int)DestinationEntityTypes.Station),
				State(3, 20, UnitStateTypes.Committed)
			};
			CallStatusAttribution.InferUnitStates(CallId, T0, T0.AddHours(1), toStation, Clearing).Select(x => x.UnitStateId).Should().Equal(1);
		}

		[Test]
		public void inference_ends_at_the_call_close()
		{
			var states = new List<UnitState> { State(1, 5, UnitStateTypes.OnScene), State(2, 90, UnitStateTypes.Committed) };

			CallStatusAttribution.InferUnitStates(CallId, T0, T0.AddHours(1), states, Clearing).Select(x => x.UnitStateId).Should().Equal(1);
		}

		[Test]
		public void paged_personnel_only_count_once_they_engage()
		{
			bool PersonClearing(ActionLog l) => CallStatusLinkage.IsClearingPersonnelStatus(l.ActionTypeId, null);
			var logs = new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, UserId = "u", ActionTypeId = (int)ActionTypes.StandingBy, Timestamp = T0.AddMinutes(1) },
				new ActionLog { ActionLogId = 2, UserId = "u", ActionTypeId = (int)ActionTypes.Responding, Timestamp = T0.AddMinutes(3) },
				new ActionLog { ActionLogId = 3, UserId = "u", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0.AddMinutes(12) },
				new ActionLog { ActionLogId = 4, UserId = "u", ActionTypeId = (int)ActionTypes.StandingBy, Timestamp = T0.AddMinutes(60) }
			};

			CallStatusAttribution.InferActionLogs(CallId, T0, T0.AddHours(2), true, logs, PersonClearing).Select(x => x.ActionLogId).Should().Equal(2, 3, 4);
			CallStatusAttribution.InferActionLogs(CallId, T0, T0.AddHours(2), false, logs, PersonClearing).Select(x => x.ActionLogId).Should().Equal(1);

			var neverEngaged = logs.Take(1).ToList();
			CallStatusAttribution.InferActionLogs(CallId, T0, T0.AddHours(2), true, neverEngaged, PersonClearing).Should().BeEmpty();
		}

		[Test]
		public void clearing_statuses_cover_built_in_and_custom_base_types()
		{
			CallStatusLinkage.IsClearingUnitState((int)UnitStateTypes.Available, null).Should().BeTrue();
			CallStatusLinkage.IsClearingUnitState((int)UnitStateTypes.Released, null).Should().BeTrue();
			CallStatusLinkage.IsClearingUnitState((int)UnitStateTypes.OnScene, null).Should().BeFalse();
			CallStatusLinkage.IsClearingUnitState(900, new Dictionary<int, int> { [900] = (int)ActionBaseTypes.Cleared }).Should().BeTrue();
			CallStatusLinkage.IsClearingUnitState(901, new Dictionary<int, int> { [901] = (int)ActionBaseTypes.None }).Should().BeFalse();

			CallStatusLinkage.IsClearingPersonnelStatus((int)ActionTypes.StandingBy, null).Should().BeTrue();
			CallStatusLinkage.IsClearingPersonnelStatus((int)ActionTypes.RespondingToStation, null).Should().BeFalse("volunteers respond to the station to pick up the apparatus");
			CallStatusLinkage.IsClearingPersonnelStatus(902, new Dictionary<int, int> { [902] = (int)ActionBaseTypes.Returning }).Should().BeTrue();
		}

		[Test]
		public void unit_times_come_from_dispatch_and_the_first_status_of_each_kind()
		{
			var dispatches = new[] { new CallDispatchUnit { CallId = CallId, UnitId = 5, DispatchedOn = T0 } };
			var states = new List<UnitState>
			{
				State(1, 1, UnitStateTypes.Responding, CallId, (int)DestinationEntityTypes.Call),
				State(2, 9, UnitStateTypes.OnScene, CallId, (int)DestinationEntityTypes.Call),
				State(3, 44, UnitStateTypes.Available, CallId, (int)DestinationEntityTypes.Call)
			};
			states[1].DestinationSource = (int)StatusDestinationSources.CarryForward;

			var row = CallUnitTimesCalculator.Compute(dispatches, states, null).Single();

			row.DispatchedOn.Should().Be(T0);
			row.EnrouteOn.Should().Be(T0.AddMinutes(1));
			row.OnSceneOn.Should().Be(T0.AddMinutes(9));
			row.StagingOn.Should().BeNull();
			row.ClearedOn.Should().Be(T0.AddMinutes(44));
			row.Source.Should().Be(CallUnitTimesSources.AutoLinked);
		}

		[Test]
		public void unit_times_mark_dispatch_only_and_inferred_rows()
		{
			var dispatches = new[]
			{
				new CallDispatchUnit { CallId = CallId, UnitId = 5, DispatchedOn = T0 },
				new CallDispatchUnit { CallId = CallId, UnitId = 6, DispatchedOn = T0 }
			};
			var inferredOnScene = State(1, 9, UnitStateTypes.OnScene, CallId, (int)DestinationEntityTypes.Call);
			inferredOnScene.DestinationSource = (int)StatusDestinationSources.Inferred;

			var rows = CallUnitTimesCalculator.Compute(dispatches, new[] { inferredOnScene }, null);

			rows.Single(x => x.UnitId == 5).Source.Should().Be(CallUnitTimesSources.Inferred);
			rows.Single(x => x.UnitId == 6).Source.Should().Be(CallUnitTimesSources.DispatchOnly);
		}
	}
}
