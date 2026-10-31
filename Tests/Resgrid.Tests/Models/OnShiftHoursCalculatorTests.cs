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
	/// On-shift hours across shift types: time is clipped to the report window, a person counts once per occurrence, and
	/// hours split by staffing source (approved or automatic) and shift type.
	/// </summary>
	[TestFixture]
	public class OnShiftHoursCalculatorTests
	{
		private static ShiftDayStaffing Occurrence(StaffedShiftTypes type, ShiftStaffingSources source, DateTime start, double hours, params ShiftDayStaffingPerson[] people)
		{
			return new ShiftDayStaffing
			{
				ShiftType = type,
				Source = source,
				Name = type.ToString(),
				Day = start.Date,
				Start = start,
				End = start.AddHours(hours),
				Personnel = people.ToList()
			};
		}

		private static ShiftDayStaffingPerson Person(string userId, int? groupId = null)
		{
			return new ShiftDayStaffingPerson { UserId = userId, DepartmentGroupId = groupId };
		}

		[Test]
		public void Hours_add_up_per_person_across_shift_types_and_staffing_sources()
		{
			var day = new DateTime(2026, 10, 1);
			var occurrences = new[]
			{
				Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Automatic, day.AddHours(7), 12, Person("a"), Person("b")),
				Occurrence(StaffedShiftTypes.Signup, ShiftStaffingSources.Manual, day.AddDays(1).AddHours(7), 10, Person("a")),
				Occurrence(StaffedShiftTypes.Workshift, ShiftStaffingSources.Automatic, day.AddDays(2), 24, Person("a"))
			};

			var people = OnShiftHoursCalculator.Calculate(occurrences, day, day.AddDays(7));

			var a = people.Single(x => x.UserId == "a");
			a.ShiftCount.Should().Be(3);
			a.TotalHours.Should().Be(46);
			a.ManualHours.Should().Be(10);
			a.AutomaticHours.Should().Be(36);
			a.ManualShiftCount.Should().Be(1);
			a.HoursFor(StaffedShiftTypes.Assigned).Should().Be(12);
			a.HoursFor(StaffedShiftTypes.Signup).Should().Be(10);
			a.HoursFor(StaffedShiftTypes.Workshift).Should().Be(24);
			a.Entries.Select(x => x.ShiftType).Should().Equal(StaffedShiftTypes.Assigned, StaffedShiftTypes.Signup, StaffedShiftTypes.Workshift);

			people.Single(x => x.UserId == "b").TotalHours.Should().Be(12);
		}

		[Test]
		public void A_shift_crossing_the_window_counts_only_the_part_inside_it()
		{
			var night = Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Automatic, new DateTime(2026, 10, 31, 19, 0, 0), 12, Person("a"));

			var october = OnShiftHoursCalculator.Calculate(new[] { night }, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1));
			var november = OnShiftHoursCalculator.Calculate(new[] { night }, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1));

			var entry = october.Single().Entries.Single();
			entry.Hours.Should().Be(5);
			entry.End.Should().Be(new DateTime(2026, 11, 1));
			entry.IsPartial.Should().BeTrue();
			november.Single().TotalHours.Should().Be(7);
		}

		[Test]
		public void A_person_in_two_slots_on_one_occurrence_counts_once_and_outside_ones_not_at_all()
		{
			var day = new DateTime(2026, 10, 1);
			var occurrences = new[]
			{
				Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Manual, day.AddHours(7), 12, Person("a", 1), Person("A", 2)),
				Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Manual, day.AddDays(-2), 12, Person("a"))
			};

			var people = OnShiftHoursCalculator.Calculate(occurrences, day, day.AddDays(1));

			people.Should().ContainSingle();
			people.Single().TotalHours.Should().Be(12);
			people.Single().Entries.Single().DepartmentGroupId.Should().Be(1);
		}

		[Test]
		public void The_group_filter_uses_the_slot_group_then_membership_for_slots_without_one()
		{
			var day = new DateTime(2026, 10, 1);
			var occurrence = Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Automatic, day.AddHours(7), 12,
				Person("in-group", 5), Person("other-group", 6), Person("member"), Person("not-member"), Person("member-elsewhere", 6));
			var filter = OnShiftHoursCalculator.GroupFilter(new HashSet<int> { 5 }, new HashSet<string> { "MEMBER", "member-elsewhere" });

			var people = OnShiftHoursCalculator.Calculate(new[] { occurrence }, day, day.AddDays(1), filter);

			// A member filling another group's slot is counted for that group, not this one.
			people.Select(x => x.UserId).Should().BeEquivalentTo(new[] { "in-group", "member" });
		}

		[Test]
		public void A_reversed_window_is_read_the_right_way_round()
		{
			var day = new DateTime(2026, 10, 1);
			var occurrence = Occurrence(StaffedShiftTypes.Assigned, ShiftStaffingSources.Automatic, day.AddHours(7), 12, Person("a"));

			OnShiftHoursCalculator.Calculate(new[] { occurrence }, day.AddDays(1), day).Single().TotalHours.Should().Be(12);
		}
	}
}
