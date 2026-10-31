using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Reporting
{
	/// <summary>One person's time on one shift occurrence, clipped to the report window.</summary>
	public class OnShiftHoursEntry
	{
		public string UserId { get; set; }

		public ShiftDayStaffing Staffing { get; set; }

		/// <summary>The group whose slot the person filled (the unit's station for a static shift).</summary>
		public int? DepartmentGroupId { get; set; }

		public int? UnitId { get; set; }

		/// <summary>Department-local start of the counted time (the later of the shift start and the report start).</summary>
		public DateTime Start { get; set; }

		/// <summary>Department-local end of the counted time (the earlier of the shift end and the report end).</summary>
		public DateTime End { get; set; }

		public double Hours { get; set; }

		public ShiftStaffingSources Source => Staffing?.Source ?? ShiftStaffingSources.Automatic;

		public StaffedShiftTypes ShiftType => Staffing?.ShiftType ?? StaffedShiftTypes.Assigned;

		/// <summary>The time only partly falls inside the report window.</summary>
		public bool IsPartial => Staffing != null && (Start > Staffing.Start || End < Staffing.End);
	}

	/// <summary>One person's on-shift hours across every shift type for a report window.</summary>
	public class OnShiftHoursPerson
	{
		public string UserId { get; set; }

		public List<OnShiftHoursEntry> Entries { get; set; } = new List<OnShiftHoursEntry>();

		public int ShiftCount => Entries.Count;

		public double TotalHours => Entries.Sum(x => x.Hours);

		/// <summary>Hours on occurrences whose staffing a supervisor set (approved).</summary>
		public double ManualHours => Entries.Where(x => x.Source == ShiftStaffingSources.Manual).Sum(x => x.Hours);

		/// <summary>Hours on occurrences staffed automatically from the shift.</summary>
		public double AutomaticHours => Entries.Where(x => x.Source == ShiftStaffingSources.Automatic).Sum(x => x.Hours);

		public int ManualShiftCount => Entries.Count(x => x.Source == ShiftStaffingSources.Manual);

		public double HoursFor(StaffedShiftTypes type) => Entries.Where(x => x.ShiftType == type).Sum(x => x.Hours);
	}

	/// <summary>The on-shift hours report: the occurrences it covers and each person's hours on them.</summary>
	public class OnShiftHoursReport
	{
		public DateTime Start { get; set; }

		public DateTime End { get; set; }

		/// <summary>Shift occurrences overlapping the window that have someone the report counts (every one without a group filter).</summary>
		public List<ShiftDayStaffing> Occurrences { get; set; } = new List<ShiftDayStaffing>();

		public List<OnShiftHoursPerson> People { get; set; } = new List<OnShiftHoursPerson>();
	}

	/// <summary>
	/// Totals on-shift hours per person from resolved shift staffing. Pure: no I/O. Every occurrence counts the time that
	/// falls inside [localStart, localEnd); a person listed twice on one occurrence (two groups) is counted once.
	/// </summary>
	public static class OnShiftHoursCalculator
	{
		/// <param name="occurrences">Resolved staffing, department-local times.</param>
		/// <param name="localStart">Report start, department local.</param>
		/// <param name="localEnd">Report end (exclusive), department local.</param>
		/// <param name="include">Which people on which occurrences to count; null counts everyone.</param>
		public static List<OnShiftHoursPerson> Calculate(IEnumerable<ShiftDayStaffing> occurrences, DateTime localStart, DateTime localEnd,
			Func<ShiftDayStaffing, ShiftDayStaffingPerson, bool> include = null)
		{
			if (localEnd < localStart)
				(localStart, localEnd) = (localEnd, localStart);

			var people = new Dictionary<string, OnShiftHoursPerson>(StringComparer.OrdinalIgnoreCase);

			foreach (var occurrence in (occurrences ?? Enumerable.Empty<ShiftDayStaffing>()).Where(x => x != null))
			{
				var start = occurrence.Start > localStart ? occurrence.Start : localStart;
				var end = occurrence.End < localEnd ? occurrence.End : localEnd;

				if (end <= start)
					continue;

				var hours = (end - start).TotalHours;
				var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (var person in (occurrence.Personnel ?? new List<ShiftDayStaffingPerson>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)))
				{
					if (counted.Contains(person.UserId) || (include != null && !include(occurrence, person)))
						continue;

					counted.Add(person.UserId);

					if (!people.TryGetValue(person.UserId, out var summary))
					{
						summary = new OnShiftHoursPerson { UserId = person.UserId };
						people.Add(person.UserId, summary);
					}

					summary.Entries.Add(new OnShiftHoursEntry
					{
						UserId = person.UserId,
						Staffing = occurrence,
						DepartmentGroupId = person.DepartmentGroupId,
						UnitId = person.UnitId,
						Start = start,
						End = end,
						Hours = hours
					});
				}
			}

			foreach (var summary in people.Values)
				summary.Entries = summary.Entries.OrderBy(x => x.Start).ThenBy(x => x.Staffing.Name).ToList();

			return people.Values.ToList();
		}

		/// <summary>
		/// Counts a person for a group the same way on-duty dispatch does: a slot tied to a group counts when that group is
		/// in <paramref name="groupIds"/>; a slot with no group counts when the person is a member of one of them.
		/// </summary>
		/// <param name="groupIds">The group and its child groups.</param>
		/// <param name="memberUserIds">Members of those groups.</param>
		public static Func<ShiftDayStaffing, ShiftDayStaffingPerson, bool> GroupFilter(ISet<int> groupIds, ISet<string> memberUserIds)
		{
			var groups = groupIds ?? new HashSet<int>();
			var members = new HashSet<string>(memberUserIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

			return (occurrence, person) => person.DepartmentGroupId.HasValue
				? groups.Contains(person.DepartmentGroupId.Value)
				: members.Contains(person.UserId);
		}
	}
}
