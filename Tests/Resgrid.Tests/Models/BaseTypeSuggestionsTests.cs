using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class BaseTypeSuggestionsTests
	{
		// Brockville's unit set ("Dispatch Status"), 2026-10-13.
		[TestCase("DISPATCHED", ActionBaseTypes.Dispatched)]
		[TestCase("EN ROUTE", ActionBaseTypes.Enroute)]
		[TestCase("ON LOCATION", ActionBaseTypes.OnScene)]
		[TestCase("TO HOSPITAL", ActionBaseTypes.Transporting)]
		[TestCase("RETURNING", ActionBaseTypes.Returning)]
		[TestCase("IN QUARTERS", ActionBaseTypes.InQuarters)]
		[TestCase("AVAIL OS", ActionBaseTypes.Available)]
		[TestCase("ON AIR", ActionBaseTypes.Available)]
		[TestCase("AT STATION", ActionBaseTypes.InQuarters)]
		[TestCase("OUT OF SERVICE", ActionBaseTypes.Unavailable)]
		[TestCase("Returning to Station", ActionBaseTypes.Returning)]
		[TestCase("Unavailable", ActionBaseTypes.Unavailable)]
		[TestCase("Not Responding", ActionBaseTypes.Unavailable)] // a unit has no "not responding"
		public void unit_status_names(string name, ActionBaseTypes expected)
		{
			BaseTypeSuggestions.SuggestStatus(name, CustomStateTypes.Unit).Should().Be((int)expected);
		}

		[Test]
		public void personnel_not_responding_stays_not_responding()
		{
			BaseTypeSuggestions.SuggestStatus("Not Responding", CustomStateTypes.Personnel).Should().Be((int)ActionBaseTypes.NotResponding);
		}

		[TestCase("REASSIGNED")]
		[TestCase("Code 7")]
		[TestCase("")]
		public void no_clear_hint_no_suggestion(string name)
		{
			BaseTypeSuggestions.SuggestStatus(name, CustomStateTypes.Unit).Should().BeNull();
		}

		[TestCase("On Duty", StaffingBaseTypes.OnShift)] // same as the On / Off Duty template
		[TestCase("Off Duty", StaffingBaseTypes.OffDuty)]
		[TestCase("Pager Only", StaffingBaseTypes.OnCall)]
		[TestCase("Personal On Duty", StaffingBaseTypes.OnShift)]
		[TestCase("Not Available", StaffingBaseTypes.Unavailable)]
		[TestCase("Vacation", StaffingBaseTypes.Unavailable)]
		public void staffing_names(string name, StaffingBaseTypes expected)
		{
			BaseTypeSuggestions.SuggestStatus(name, CustomStateTypes.Staffing).Should().Be((int)expected);
		}

		[TestCase("Structure Fire", CallTypeClasses.Fire)]
		[TestCase("MVA w/ Entrapment", CallTypeClasses.TrafficCollision)]
		[TestCase("Fire Alarm", CallTypeClasses.Alarm)]
		[TestCase("Medical Emergency", CallTypeClasses.Medical)]
		[TestCase("Mutual Aid Fire", CallTypeClasses.MutualAid)]
		public void call_type_names(string name, CallTypeClasses expected)
		{
			BaseTypeSuggestions.SuggestCallTypeClass(name).Should().Be((int)expected);
		}

		[TestCase("Pumper", UnitTypeClasses.Engine)]
		[TestCase("Pumper/Tanker", UnitTypeClasses.Tanker)]
		[TestCase("Aerial", UnitTypeClasses.Aerial)]
		[TestCase("Fire Chief", UnitTypeClasses.Command)]
		[TestCase("Medical Unit", UnitTypeClasses.MedicalResponse)]
		[TestCase("Crash Tender", UnitTypeClasses.Engine)] // airport rescue fire apparatus, not a water tender
		public void unit_type_names(string name, UnitTypeClasses expected)
		{
			BaseTypeSuggestions.SuggestUnitTypeClass(name).Should().Be((int)expected);
		}

		[TestCase("Emergency", CallPriority.Emergency)]
		[TestCase("Non-Emergency", CallPriority.Low)]
		[TestCase("Urgent", CallPriority.High)]
		[TestCase("Routine Transfer", CallPriority.Low)]
		public void priority_names(string name, CallPriority expected)
		{
			BaseTypeSuggestions.SuggestPriority(name).Should().Be((int)expected);
		}
	}
}
