using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P4 shared trigger engine: the criteria run cards and protocols both gain (zone, local schedule, keywords, POI type at the
	/// location, intake classification), with one evaluator. Old triggers without criteria are covered by the characterization tests.
	/// </summary>
	[TestFixture]
	public class DispatchTriggerEngineTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

		private static DispatchTriggerFacts Call(Action<DispatchTriggerFacts> change = null)
		{
			var call = new DispatchTriggerFacts
			{
				Priority = 3,
				CallTypeId = 7,
				CallTypeName = "Medical",
				AtUtc = Now,
				LocalTime = new DateTime(2026, 11, 25, 10, 0, 0), // Wednesday 10:00 department time
				Nature = "<p>Elderly male, <strong>chest pain</strong> &amp; sweating</p>",
				Notes = "Caller is the daughter",
				GeofenceGroupIds = new HashSet<int> { 40 },
				LocationPoiTypeId = 9,
				CallSource = (int)CallSources.Intake
			};
			change?.Invoke(call);
			return call;
		}

		private static DispatchProtocolTrigger Trigger(int type, Action<DispatchProtocolTrigger> change)
		{
			var trigger = new DispatchProtocolTrigger { DispatchProtocolTriggerId = 1, Type = type, Priority = 3, CallTypeId = 7 };
			change(trigger);
			return trigger;
		}

		private static bool Matches(DispatchProtocolTrigger trigger, DispatchTriggerFacts call) => DispatchTriggerEngine.Matches(DispatchTriggerEngine.ForProtocol(trigger), call);

		[Test]
		public void A_zone_trigger_needs_the_call_inside_that_groups_geofence()
		{
			var trigger = Trigger(1, t => t.GeofenceGroupId = 40);
			Matches(trigger, Call()).Should().BeTrue();
			Matches(trigger, Call(c => c.GeofenceGroupIds = new HashSet<int> { 41 })).Should().BeFalse();
			Matches(trigger, Call(c => c.GeofenceGroupIds = null)).Should().BeFalse("a call without a usable location is in no zone");
		}

		[Test]
		public void A_schedule_uses_department_local_days_and_times_and_runs_overnight()
		{
			var weekdaysDaytime = Trigger(0, t => { t.DaysOfWeek = 62; t.StartMinute = 8 * 60; t.EndMinute = 18 * 60; });
			Matches(weekdaysDaytime, Call()).Should().BeTrue("Wednesday 10:00");
			Matches(weekdaysDaytime, Call(c => c.LocalTime = new DateTime(2026, 11, 25, 18, 0, 0))).Should().BeFalse("the end is exclusive");
			Matches(weekdaysDaytime, Call(c => c.LocalTime = new DateTime(2026, 11, 28, 10, 0, 0))).Should().BeFalse("Saturday");
			Matches(weekdaysDaytime, Call(c => c.LocalTime = null)).Should().BeFalse("no local time, no schedule match");

			var fridayNight = Trigger(0, t => { t.DaysOfWeek = 1 << (int)DayOfWeek.Friday; t.StartMinute = 22 * 60; t.EndMinute = 6 * 60; });
			Matches(fridayNight, Call(c => c.LocalTime = new DateTime(2026, 11, 27, 23, 30, 0))).Should().BeTrue("Friday 23:30");
			Matches(fridayNight, Call(c => c.LocalTime = new DateTime(2026, 11, 28, 2, 0, 0))).Should().BeTrue("Saturday 02:00 belongs to Friday's night");
			Matches(fridayNight, Call(c => c.LocalTime = new DateTime(2026, 11, 29, 2, 0, 0))).Should().BeFalse("Sunday 02:00 belongs to Saturday's night");
		}

		[Test]
		public void Keywords_match_the_nature_or_notes_case_insensitively_without_markup()
		{
			Matches(Trigger(1, t => t.Keywords = "CHEST PAIN"), Call()).Should().BeTrue();
			Matches(Trigger(1, t => t.Keywords = "stroke, daughter"), Call()).Should().BeTrue("any one word, notes included");
			Matches(Trigger(1, t => t.Keywords = "pain & sweating"), Call()).Should().BeTrue("entities are decoded");
			Matches(Trigger(1, t => t.Keywords = "strong"), Call()).Should().BeFalse("tags are not text");
			Matches(Trigger(1, t => t.Keywords = "stroke"), Call()).Should().BeFalse();
			DispatchTriggerEngine.NormalizeKeywords(" chest  pain ;\nstroke,, CHEST PAIN ").Should().Be("chest pain, stroke");
		}

		[Test]
		public void The_poi_type_and_the_intake_classification_are_checked()
		{
			Matches(Trigger(1, t => t.PoiTypeId = 9), Call()).Should().BeTrue();
			Matches(Trigger(1, t => t.PoiTypeId = 8), Call()).Should().BeFalse();
			Matches(Trigger(1, t => t.PoiTypeId = 9), Call(c => c.LocationPoiTypeId = null)).Should().BeFalse();

			Matches(Trigger(1, t => t.CallSources = "6,7"), Call()).Should().BeTrue();
			Matches(Trigger(1, t => t.CallSources = "1"), Call()).Should().BeFalse();
			Matches(Trigger(1, t => t.CallSources = "1"), Call(c => c.CallSource = 0)).Should().BeTrue("a form that leaves the source unset is the dispatcher's");
		}

		[Test]
		public void Every_criterion_must_hold_together_with_the_basis()
		{
			var trigger = Trigger(2, t => { t.GeofenceGroupId = 40; t.Keywords = "chest pain"; t.PoiTypeId = 9; });
			Matches(trigger, Call()).Should().BeTrue();
			Matches(trigger, Call(c => c.Priority = 1)).Should().BeFalse("the priority basis still applies");
			Matches(trigger, Call(c => c.Nature = "fall")).Should().BeFalse();
		}

		[Test]
		public void A_conditions_only_trigger_matches_any_call_its_criteria_allow_and_needs_one()
		{
			Matches(Trigger(3, t => { t.Priority = null; t.CallTypeId = null; t.Keywords = "chest pain"; }), Call(c => { c.Priority = 0; c.CallTypeId = null; })).Should().BeTrue();
			Matches(Trigger(3, t => { }), Call()).Should().BeFalse("a conditions trigger with no condition never matches");

			DispatchTriggerEngine.Validate(3, new DispatchProtocolTrigger()).Should().ContainSingle();
			DispatchTriggerEngine.Validate(3, new DispatchProtocolTrigger { Keywords = "x" }).Should().BeEmpty();
			DispatchTriggerEngine.Validate(0, new DispatchProtocolTrigger { StartMinute = 60 }).Should().ContainSingle("a window needs both ends");
			DispatchTriggerEngine.Validate(0, new DispatchProtocolTrigger { StartMinute = 60, EndMinute = 60 }).Should().ContainSingle();
			DispatchTriggerEngine.Validate(0, new DispatchProtocolTrigger { CallSources = "99" }).Should().ContainSingle();
		}

		[Test]
		public void A_narrower_trigger_is_more_specific_for_run_cards_and_protocols_alike()
		{
			var priorityAndType = new RunCard { RunCardId = 1, Triggers = new List<RunCardTrigger> { new RunCardTrigger { TriggerType = 2, Priority = 3, CallTypeId = 7 } } };
			var typeInZone = new RunCard { RunCardId = 2, Triggers = new List<RunCardTrigger> { new RunCardTrigger { TriggerType = 1, CallTypeId = 7, GeofenceGroupId = 40 } } };

			RunCardsService.GetTriggerMatchSpecificity(priorityAndType, Call()).Should().Be(3);
			RunCardsService.GetTriggerMatchSpecificity(typeInZone, Call()).Should().Be(5);
			RunCardsService.GetTriggerMatchSpecificity(typeInZone, Call(c => c.GeofenceGroupIds = new HashSet<int>())).Should().BeNull();

			var reason = ProtocolTriggerMatcher.MostSpecific(new[]
			{
				new DispatchProtocolTrigger { DispatchProtocolTriggerId = 1, Type = 2 },
				new DispatchProtocolTrigger { DispatchProtocolTriggerId = 2, Type = 1, Keywords = "chest pain" }
			});
			reason.DispatchProtocolTriggerId.Should().Be(2);
		}
	}
}
