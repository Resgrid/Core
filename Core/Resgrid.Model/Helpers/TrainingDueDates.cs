using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// The one place training due moments are computed (T-D19, R-T8). A due date is the end of a department-local day,
	/// stored in UTC on each assignment (TrainingUser.DueOn). The dashboard widget, the search projection, the notifier and
	/// the outstanding list all compare through here, so a training never shows overdue a day early and a zone with a
	/// 45-minute offset behaves like any other.
	/// </summary>
	public static class TrainingDueDates
	{
		/// <summary>Days a member gets when assigned after a fixed due date has passed (R-T8).</summary>
		public const int LateJoinerDueDays = 30;

		/// <summary>The widest reminder lead, in days (the notifier looks this far ahead).</summary>
		public const int MaxReminderLeadDays = 60;

		/// <summary>The default reminder lead: one day before the due moment.</summary>
		public const string DefaultReminderLeadDaysCsv = "1";

		/// <summary>The last second of the department-local day <paramref name="localDate"/>, in UTC.</summary>
		public static DateTime EndOfLocalDayUtc(DateTime localDate, Department department)
		{
			var end = DateTime.SpecifyKind(localDate.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Unspecified);
			return DateTime.SpecifyKind(end.DepartmentLocalToUtc(department), DateTimeKind.Utc);
		}

		/// <summary>The first moment of the department-local day <paramref name="localDate"/>, in UTC.</summary>
		public static DateTime StartOfLocalDayUtc(DateTime localDate, Department department)
		{
			var start = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
			return DateTime.SpecifyKind(start.DepartmentLocalToUtc(department), DateTimeKind.Utc);
		}

		/// <summary>The department-local calendar date of a UTC moment.</summary>
		public static DateTime LocalDate(DateTime utc, Department department)
		{
			return DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department).Date;
		}

		/// <summary>
		/// The training's fixed due moment: <see cref="Training.ToBeCompletedBy"/> holds the department-local date the officer
		/// picked (its time is ignored), so the moment is the end of that local day.
		/// </summary>
		public static DateTime? FixedDueUtc(Training training, Department department)
		{
			if (training?.ToBeCompletedBy == null)
				return null;

			return EndOfLocalDayUtc(training.ToBeCompletedBy.Value, department);
		}

		/// <summary>
		/// The due moment of an assignment made at <paramref name="assignedOnUtc"/>: N days after it for a relative rule, the
		/// fixed date otherwise; someone assigned after a fixed date has passed gets <see cref="LateJoinerDueDays"/> from
		/// assignment, never an assignment that is overdue on arrival. Null when the training has no due rule.
		/// </summary>
		public static DateTime? DueForAssignment(Training training, DateTime assignedOnUtc, Department department)
		{
			if (training == null)
				return null;

			var assignedLocal = LocalDate(assignedOnUtc, department);

			if (training.DueDays.HasValue && training.DueDays.Value > 0)
				return EndOfLocalDayUtc(assignedLocal.AddDays(training.DueDays.Value), department);

			var fixedDue = FixedDueUtc(training, department);
			if (!fixedDue.HasValue)
				return null;

			if (fixedDue.Value <= assignedOnUtc)
				return EndOfLocalDayUtc(assignedLocal.AddDays(LateJoinerDueDays), department);

			return fixedDue;
		}

		/// <summary>
		/// The assignment's due moment with leave applied. Rows from before T0 carry no DueOn and fall back to the training's
		/// fixed date. Null when nothing is due.
		/// </summary>
		public static DateTime? EffectiveDueUtc(Training training, TrainingUser assignment, Department department, IEnumerable<MemberLeavePeriod> leaves = null)
		{
			var due = assignment?.DueOn ?? FixedDueUtc(training, department);
			if (!due.HasValue)
				return null;

			return ApplyLeave(due.Value, assignment?.AssignedOn, leaves);
		}

		/// <summary>
		/// Shifts a due moment by the leave the member took while it was running (R-C2): every leave period that began
		/// before the (already shifted) due moment and ended after the assignment was made adds the part of it that fell
		/// after the assignment. A leave still running counts its whole planned length, so the due date the member sees on
		/// return is already the shifted one.
		/// </summary>
		public static DateTime ApplyLeave(DateTime dueUtc, DateTime? assignedOnUtc, IEnumerable<MemberLeavePeriod> leaves)
		{
			if (leaves == null)
				return dueUtc;

			var due = dueUtc;
			var from = assignedOnUtc ?? DateTime.MinValue;

			foreach (var leave in leaves.Where(l => l != null && l.EndsOn > l.StartsOn).OrderBy(l => l.StartsOn))
			{
				if (leave.StartsOn >= due || leave.EndsOn <= from)
					continue;

				var start = leave.StartsOn > from ? leave.StartsOn : from;
				due = due.Add(leave.EndsOn - start);
			}

			return due;
		}

		/// <summary>True while a leave period runs.</summary>
		public static bool IsOnLeave(DateTime utcNow, IEnumerable<MemberLeavePeriod> leaves)
		{
			return leaves != null && leaves.Any(l => l != null && l.StartsOn <= utcNow && l.EndsOn > utcNow);
		}

		/// <summary>True when the due moment has passed.</summary>
		public static bool IsOverdue(DateTime? dueUtc, DateTime utcNow) => dueUtc.HasValue && dueUtc.Value < utcNow;

		/// <summary>True when the due moment is within the next <paramref name="window"/> and has not passed.</summary>
		public static bool IsDueWithin(DateTime? dueUtc, DateTime utcNow, TimeSpan window) =>
			dueUtc.HasValue && dueUtc.Value >= utcNow && dueUtc.Value <= utcNow.Add(window);

		/// <summary>Where a member stands on an assignment.</summary>
		public static TrainingAssignmentStates StateOf(Training training, TrainingUser assignment, DateTime? effectiveDueUtc, bool onLeave, DateTime utcNow)
		{
			if (assignment == null)
				return TrainingAssignmentStates.NotStarted;
			if (assignment.RemovedOn.HasValue)
				return TrainingAssignmentStates.Removed;
			if (assignment.Complete)
				return TrainingAssignmentStates.Complete;
			if (assignment.IsExemptOn(utcNow))
				return TrainingAssignmentStates.Exempt;
			if (training != null && !training.HasStarted(utcNow))
				return TrainingAssignmentStates.Scheduled;
			if (onLeave)
				return TrainingAssignmentStates.OnLeave;
			if (IsOverdue(effectiveDueUtc, utcNow))
				return TrainingAssignmentStates.Overdue;
			if (assignment.Passed == false)
				return TrainingAssignmentStates.Failed;
			if (assignment.Viewed)
				return TrainingAssignmentStates.InProgress;
			return TrainingAssignmentStates.NotStarted;
		}

		/// <summary>Reminder lead days: positive, de-duplicated, largest first; one day when the CSV is empty or malformed.</summary>
		public static IReadOnlyList<int> ParseLeadDays(string csv)
		{
			var days = new SortedSet<int>();
			foreach (var part in (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
				if (int.TryParse(part.Trim(), out var day) && day > 0 && day <= MaxReminderLeadDays)
					days.Add(day);
			if (days.Count == 0)
				days.Add(1);
			return days.Reverse().ToList();
		}

		/// <summary>Normalized CSV for storage (null when it is the default).</summary>
		public static string NormalizeLeadDaysCsv(string csv)
		{
			var normalized = string.Join(",", ParseLeadDays(csv));
			return normalized == DefaultReminderLeadDaysCsv ? null : normalized;
		}

		/// <summary>The first repeat after <paramref name="fromUtc"/>.</summary>
		public static DateTime? NextRepeatOn(int? repeatMonths, DateTime fromUtc)
		{
			if (!repeatMonths.HasValue || repeatMonths.Value <= 0)
				return null;
			return fromUtc.AddMonths(repeatMonths.Value);
		}
	}
}
