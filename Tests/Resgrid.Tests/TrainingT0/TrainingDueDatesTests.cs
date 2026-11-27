using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// TrainingDueDates (T-D19, R-T8, R-C2): a due date is the end of a department-local day stored in UTC, the same in any
	/// zone (45-minute offsets included); relative and late-joiner rules; leave pauses and shifts due dates.
	/// </summary>
	[TestFixture]
	public class TrainingDueDatesTests
	{
		private static Department Zone(string timeZone) => new Department { DepartmentId = 10, TimeZone = timeZone };

		private static readonly Department Nepal = Zone("Nepal Standard Time");       // UTC+5:45, no daylight saving
		private static readonly Department Chatham = Zone("Chatham Islands Standard Time"); // UTC+12:45, +13:45 in the southern summer
		private static readonly Department Eastern = Zone("Eastern Standard Time");   // UTC-5, -4 in the northern summer

		private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

		[Test]
		public void The_end_of_a_local_day_is_stored_in_utc_for_45_minute_and_negative_offsets()
		{
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1), Nepal).Should().Be(Utc(2026, 12, 1, 18, 14, 59));
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1), Chatham).Should().Be(Utc(2026, 12, 1, 10, 14, 59), "+13:45 in December");
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 7, 1), Chatham).Should().Be(Utc(2026, 7, 1, 11, 14, 59), "+12:45 in July");
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1), Eastern).Should().Be(Utc(2026, 12, 2, 4, 59, 59));
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1), Eastern).Kind.Should().Be(DateTimeKind.Utc);
		}

		[Test]
		public void A_time_on_the_picked_date_is_ignored()
		{
			TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1, 9, 30, 0), Nepal).Should().Be(TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 1), Nepal));
		}

		[Test]
		public void The_start_of_a_local_day_and_the_local_date_of_a_moment_round_trip()
		{
			TrainingDueDates.StartOfLocalDayUtc(new DateTime(2026, 12, 1), Nepal).Should().Be(Utc(2026, 11, 30, 18, 15, 0));
			TrainingDueDates.LocalDate(Utc(2026, 11, 30, 18, 20), Nepal).Should().Be(new DateTime(2026, 12, 1));
			TrainingDueDates.LocalDate(Utc(2026, 11, 30, 18, 10), Nepal).Should().Be(new DateTime(2026, 11, 30));
		}

		[Test]
		public void A_fixed_date_is_due_at_the_end_of_that_local_day_not_at_utc_midnight()
		{
			var training = new Training { ToBeCompletedBy = new DateTime(2026, 12, 1) };

			var due = TrainingDueDates.FixedDueUtc(training, Eastern);

			due.Should().Be(Utc(2026, 12, 2, 4, 59, 59));
			// The old comparison treated the stored local midnight as UTC and showed it overdue a day early.
			TrainingDueDates.IsOverdue(due, Utc(2026, 12, 1, 23, 0)).Should().BeFalse();
			TrainingDueDates.IsOverdue(due, Utc(2026, 12, 2, 5, 0)).Should().BeTrue();
			TrainingDueDates.FixedDueUtc(new Training(), Eastern).Should().BeNull();
		}

		[Test]
		public void A_relative_rule_is_due_n_days_after_the_local_assignment_day()
		{
			var training = new Training { DueDays = 14 };

			// 20:00 UTC on 1 December is already 2 December in Kathmandu.
			TrainingDueDates.DueForAssignment(training, Utc(2026, 12, 1, 20, 0), Nepal).Should().Be(Utc(2026, 12, 16, 18, 14, 59));
		}

		[Test]
		public void A_fixed_date_applies_until_it_passes_and_a_late_joiner_gets_thirty_days()
		{
			var training = new Training { ToBeCompletedBy = new DateTime(2026, 12, 20) };

			TrainingDueDates.DueForAssignment(training, Utc(2026, 12, 1), Eastern).Should().Be(Utc(2026, 12, 21, 4, 59, 59));

			var late = TrainingDueDates.DueForAssignment(training, Utc(2026, 12, 22, 15, 0), Eastern);
			late.Should().Be(TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 22).AddDays(TrainingDueDates.LateJoinerDueDays), Eastern));
			late.Value.Should().BeAfter(Utc(2026, 12, 22, 15, 0), "never overdue on arrival");

			TrainingDueDates.DueForAssignment(new Training(), Utc(2026, 12, 1), Eastern).Should().BeNull();
			TrainingDueDates.DueForAssignment(null, Utc(2026, 12, 1), Eastern).Should().BeNull();
		}

		[Test]
		public void The_effective_due_falls_back_to_the_fixed_date_for_rows_from_before_t0()
		{
			var training = new Training { ToBeCompletedBy = new DateTime(2026, 12, 1) };

			TrainingDueDates.EffectiveDueUtc(training, new TrainingUser(), Eastern).Should().Be(Utc(2026, 12, 2, 4, 59, 59));
			TrainingDueDates.EffectiveDueUtc(training, new TrainingUser { DueOn = Utc(2027, 1, 1) }, Eastern).Should().Be(Utc(2027, 1, 1));
			TrainingDueDates.EffectiveDueUtc(new Training(), new TrainingUser(), Eastern).Should().BeNull();
		}

		private static MemberLeavePeriod Leave(DateTime starts, DateTime ends) => new MemberLeavePeriod { StartsOn = starts, EndsOn = ends };

		[Test]
		public void Leave_shifts_the_due_date_by_the_part_of_it_after_the_assignment()
		{
			var assigned = Utc(2026, 11, 1);
			var due = Utc(2026, 12, 1);

			TrainingDueDates.ApplyLeave(due, assigned, null).Should().Be(due);
			TrainingDueDates.ApplyLeave(due, assigned, new[] { Leave(Utc(2026, 10, 1), Utc(2026, 10, 10)) }).Should().Be(due, "leave before the assignment");
			TrainingDueDates.ApplyLeave(due, assigned, new[] { Leave(Utc(2026, 11, 10), Utc(2026, 11, 20)) }).Should().Be(due.AddDays(10), "leave inside the window");
			TrainingDueDates.ApplyLeave(due, assigned, new[] { Leave(Utc(2026, 10, 25), Utc(2026, 11, 5)) }).Should().Be(due.AddDays(4), "only the part after the assignment");
			TrainingDueDates.ApplyLeave(due, assigned, new[] { Leave(Utc(2026, 12, 5), Utc(2026, 12, 15)) }).Should().Be(due, "leave after the due date");
		}

		[Test]
		public void A_leave_that_starts_inside_an_already_shifted_window_counts_too()
		{
			var assigned = Utc(2026, 11, 1);
			var due = Utc(2026, 12, 1);

			var shifted = TrainingDueDates.ApplyLeave(due, assigned, new[]
			{
				Leave(Utc(2026, 12, 3), Utc(2026, 12, 6)),   // after the original due, inside the shifted one: listed first on purpose
				Leave(Utc(2026, 11, 10), Utc(2026, 11, 20))
			});

			shifted.Should().Be(due.AddDays(13));
		}

		[Test]
		public void A_running_leave_counts_its_whole_planned_length_and_pauses_reminders()
		{
			var now = Utc(2026, 11, 15);
			var leaves = new List<MemberLeavePeriod> { Leave(Utc(2026, 11, 10), Utc(2026, 11, 30)) };

			TrainingDueDates.IsOnLeave(now, leaves).Should().BeTrue();
			TrainingDueDates.IsOnLeave(Utc(2026, 12, 1), leaves).Should().BeFalse();
			TrainingDueDates.IsOnLeave(now, null).Should().BeFalse();
			TrainingDueDates.ApplyLeave(Utc(2026, 11, 20), Utc(2026, 11, 1), leaves).Should().Be(Utc(2026, 12, 10));
		}

		[Test]
		public void The_state_of_an_assignment_follows_the_documented_precedence()
		{
			var now = Utc(2026, 11, 24, 12, 0);
			var training = new Training();
			var past = now.AddDays(-1);

			TrainingDueDates.StateOf(training, new TrainingUser { RemovedOn = past, Complete = true }, past, true, now).Should().Be(TrainingAssignmentStates.Removed);
			TrainingDueDates.StateOf(training, new TrainingUser { Complete = true, ExemptOn = past }, past, false, now).Should().Be(TrainingAssignmentStates.Complete);
			TrainingDueDates.StateOf(training, new TrainingUser { ExemptOn = past }, past, true, now).Should().Be(TrainingAssignmentStates.Exempt);
			TrainingDueDates.StateOf(training, new TrainingUser { ExemptOn = past.AddDays(-5), ExemptUntil = past }, null, false, now).Should().Be(TrainingAssignmentStates.NotStarted, "an expired exemption");
			TrainingDueDates.StateOf(new Training { StartsOn = now.AddDays(2) }, new TrainingUser(), past, true, now).Should().Be(TrainingAssignmentStates.Scheduled);
			TrainingDueDates.StateOf(training, new TrainingUser(), past, true, now).Should().Be(TrainingAssignmentStates.OnLeave);
			TrainingDueDates.StateOf(training, new TrainingUser { Passed = false }, past, false, now).Should().Be(TrainingAssignmentStates.Overdue);
			TrainingDueDates.StateOf(training, new TrainingUser { Passed = false, Viewed = true }, now.AddDays(3), false, now).Should().Be(TrainingAssignmentStates.Failed);
			TrainingDueDates.StateOf(training, new TrainingUser { Viewed = true }, null, false, now).Should().Be(TrainingAssignmentStates.InProgress);
			TrainingDueDates.StateOf(training, new TrainingUser(), null, false, now).Should().Be(TrainingAssignmentStates.NotStarted);
		}

		[Test]
		public void Due_within_and_overdue_are_exclusive_around_now()
		{
			var now = Utc(2026, 11, 24, 12, 0);

			TrainingDueDates.IsDueWithin(now.AddHours(20), now, TimeSpan.FromDays(1)).Should().BeTrue();
			TrainingDueDates.IsDueWithin(now.AddHours(30), now, TimeSpan.FromDays(1)).Should().BeFalse();
			TrainingDueDates.IsDueWithin(now.AddHours(-1), now, TimeSpan.FromDays(1)).Should().BeFalse();
			TrainingDueDates.IsDueWithin(null, now, TimeSpan.FromDays(1)).Should().BeFalse();
			TrainingDueDates.IsOverdue(now.AddMinutes(-1), now).Should().BeTrue();
			TrainingDueDates.IsOverdue(null, now).Should().BeFalse();
		}

		[Test]
		public void Lead_days_are_parsed_like_the_certification_csv_with_a_one_day_default()
		{
			TrainingDueDates.ParseLeadDays("7, 1;14 bogus 0 -3 61 7").Should().Equal(14, 7, 1);
			TrainingDueDates.ParseLeadDays(null).Should().Equal(1);
			TrainingDueDates.ParseLeadDays("nonsense").Should().Equal(1);
			TrainingDueDates.NormalizeLeadDaysCsv("1").Should().BeNull();
			TrainingDueDates.NormalizeLeadDaysCsv("1, 7").Should().Be("7,1");
		}

		[Test]
		public void The_next_repeat_is_one_interval_later()
		{
			TrainingDueDates.NextRepeatOn(null, Utc(2026, 1, 31)).Should().BeNull();
			TrainingDueDates.NextRepeatOn(0, Utc(2026, 1, 31)).Should().BeNull();
			TrainingDueDates.NextRepeatOn(12, Utc(2026, 1, 31)).Should().Be(Utc(2027, 1, 31));
		}
	}
}
