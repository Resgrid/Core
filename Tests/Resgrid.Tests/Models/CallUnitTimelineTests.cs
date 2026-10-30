using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// A unit's whole part in a call (the Belgian EMS ask: dispatch until back in quarters, transport and hospital included),
	/// as the Call Unit Times report shows it.
	/// </summary>
	[TestFixture]
	public class CallUnitTimelineTests
	{
		private const int CallId = 42;
		private const int UnitId = 5;
		private static readonly DateTime T0 = new DateTime(2026, 10, 6, 22, 32, 0, DateTimeKind.Utc);

		// Custom statuses as an EMS department sets them up, by base type.
		private const int Transporting = 101;
		private const int AtHospital = 102;
		private const int Returning = 103;
		private const int InQuarters = 104;
		private const int Responding = 105;

		private static readonly Dictionary<int, int> BaseTypes = new Dictionary<int, int>
		{
			{ Transporting, (int)ActionBaseTypes.Transporting },
			{ AtHospital, (int)ActionBaseTypes.AtHospital },
			{ Returning, (int)ActionBaseTypes.Returning },
			{ InQuarters, (int)ActionBaseTypes.InQuarters },
			{ Responding, (int)ActionBaseTypes.Responding }
		};

		private static bool FinishesCall(UnitState state) => UnitCallInvolvement.HasFinishedCall(state.State, BaseTypes);

		private static UnitState State(int id, int state, int minutes, int? callId = null, StatusDestinationSources? source = null,
			int? destinationType = null, int? destinationId = null, bool automation = false)
		{
			return new UnitState
			{
				UnitStateId = id,
				UnitId = UnitId,
				State = state,
				Timestamp = T0.AddMinutes(minutes),
				DestinationId = callId ?? destinationId,
				DestinationType = callId.HasValue ? (int)DestinationEntityTypes.Call : destinationType,
				DestinationSource = source.HasValue ? (int)source.Value : (int?)null,
				SetByOrigin = automation ? (int)StatusSetOrigins.DispatchAutomation : (int?)null
			};
		}

		private static List<UnitState> Build(IEnumerable<UnitState> onRecord, IEnumerable<UnitState> history, int callEndMinutes = 6, int? nextDispatchMinutes = null)
		{
			return CallUnitTimeline.Build(CallId, T0, T0.AddMinutes(callEndMinutes), onRecord, history,
				nextDispatchMinutes.HasValue ? T0.AddMinutes(nextDispatchMinutes.Value) : (DateTime?)null, FinishesCall);
		}

		[Test]
		public void the_transport_hospital_and_return_after_the_call_closed_follow_until_back_in_quarters()
		{
			var responding = State(1, Responding, 2, CallId, StatusDestinationSources.Explicit);
			var history = new[]
			{
				responding,
				State(2, Transporting, 12),
				// At the hospital: a POI destination, which ends the call's own record.
				State(3, AtHospital, 25, destinationType: (int)DestinationEntityTypes.Poi, destinationId: 900),
				State(4, Returning, 50),
				State(5, InQuarters, 70),
				State(6, Responding, 300)
			};

			var timeline = Build(new[] { responding }, history);

			timeline.Select(x => x.UnitStateId).Should().Equal(1, 2, 3, 4, 5);
			timeline.Skip(1).Should().OnlyContain(x => x.DestinationSource == (int)StatusDestinationSources.FollowOn);
			timeline[2].DestinationType.Should().Be((int)DestinationEntityTypes.Poi, "the copy keeps where the unit was");
			history[1].DestinationSource.Should().BeNull("the unit's own rows are never changed");
		}

		[Test]
		public void a_release_at_the_close_does_not_end_it_when_the_crew_keeps_working()
		{
			var onRecord = new[]
			{
				State(1, Responding, 2, CallId, StatusDestinationSources.Explicit),
				State(2, InQuarters, 6, CallId, StatusDestinationSources.Explicit, automation: true)
			};
			var history = onRecord.Concat(new[] { State(3, Transporting, 8), State(4, InQuarters, 60) });

			Build(onRecord, history).Select(x => x.UnitStateId).Should().Equal(1, 2, 3, 4);
		}

		[Test]
		public void a_release_at_the_close_stands_when_the_crews_next_status_is_back_in_service_too()
		{
			// Nothing after the release until the crew set In Quarters the next morning: that is not this call's.
			var onRecord = new[]
			{
				State(1, Responding, 2, CallId, StatusDestinationSources.Explicit),
				State(2, InQuarters, 6, CallId, StatusDestinationSources.Explicit, automation: true)
			};
			var history = onRecord.Concat(new[] { State(3, InQuarters, 600), State(4, Transporting, 620) });

			Build(onRecord, history).Select(x => x.UnitStateId).Should().Equal(1, 2);
		}

		[Test]
		public void a_crew_putting_the_unit_back_in_service_ends_it()
		{
			var onRecord = new[]
			{
				State(1, Responding, 2, CallId, StatusDestinationSources.Explicit),
				State(2, InQuarters, 20, CallId, StatusDestinationSources.Explicit)
			};
			var history = onRecord.Concat(new[] { State(3, Transporting, 30) });

			Build(onRecord, history).Select(x => x.UnitStateId).Should().Equal(1, 2);
		}

		[Test]
		public void it_stops_before_another_calls_status_or_another_dispatch()
		{
			var responding = State(1, Responding, 2, CallId, StatusDestinationSources.Explicit);

			var otherCall = new[] { responding, State(2, Transporting, 10), State(3, Responding, 20, 43), State(4, InQuarters, 60) };
			Build(new[] { responding }, otherCall).Select(x => x.UnitStateId).Should().Equal(1, 2);

			var redispatched = new[] { responding, State(2, Transporting, 10), State(3, Transporting, 30), State(4, InQuarters, 60) };
			Build(new[] { responding }, redispatched, nextDispatchMinutes: 25).Select(x => x.UnitStateId).Should().Equal(1, 2);
		}

		[Test]
		public void it_stops_twelve_hours_after_the_call()
		{
			var responding = State(1, Responding, 2, CallId, StatusDestinationSources.Explicit);
			var history = new[] { responding, State(2, Transporting, 30), State(3, InQuarters, 6 + 12 * 60 + 1) };

			Build(new[] { responding }, history).Select(x => x.UnitStateId).Should().Equal(1, 2);
		}

		[Test]
		public void the_calls_own_statuses_are_always_kept()
		{
			// Back in quarters, then a status sent for this call again later: the record wins over the timeline's end.
			var onRecord = new[]
			{
				State(1, Responding, 2, CallId, StatusDestinationSources.Explicit),
				State(2, InQuarters, 20, CallId, StatusDestinationSources.Explicit),
				State(4, Returning, 40, CallId, StatusDestinationSources.Explicit)
			};
			var history = onRecord.Concat(new[] { State(3, Transporting, 30) });

			Build(onRecord, history).Select(x => x.UnitStateId).Should().Equal(1, 2, 4);
		}

		[Test]
		public void the_report_row_carries_the_history_durations_and_the_time_committed()
		{
			var responding = State(1, Responding, 2, CallId, StatusDestinationSources.Explicit);
			var timeline = Build(new[] { responding }, new[] { responding, State(2, Transporting, 12), State(3, AtHospital, 25), State(4, InQuarters, 70) });
			var dispatches = new[] { new CallDispatchUnit { CallId = CallId, UnitId = UnitId, DispatchedOn = T0 } };

			var row = CallUnitTimesCalculator.Compute(dispatches, null, BaseTypes, timeline).Single();

			row.EnrouteOn.Should().Be(T0.AddMinutes(2), "the key times still come from the call's record");
			row.Source.Should().Be(CallUnitTimesSources.Explicit);
			row.Timeline.Select(x => x.State).Should().Equal(Responding, Transporting, AtHospital, InQuarters);
			row.Timeline.Select(x => x.DurationSeconds).Should().Equal(600d, 780d, 2700d, null);
			row.Timeline[1].IsFollowOn.Should().BeTrue();
			row.InServiceOn.Should().Be(T0.AddMinutes(70));
			row.CommittedSeconds.Should().Be(70 * 60);
		}

		[Test]
		public void a_unit_still_working_has_no_back_in_service_time()
		{
			var responding = State(1, Responding, 2, CallId, StatusDestinationSources.Explicit);
			var timeline = Build(new[] { responding }, new[] { responding, State(2, Transporting, 12) });

			var row = CallUnitTimesCalculator.Compute(new[] { new CallDispatchUnit { CallId = CallId, UnitId = UnitId, DispatchedOn = T0 } }, null, BaseTypes, timeline).Single();

			row.InServiceOn.Should().BeNull();
			row.CommittedSeconds.Should().BeNull();
		}
	}
}
