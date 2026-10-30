using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>Where a shift day's staffing comes from.</summary>
	public enum ShiftStaffingSources
	{
		/// <summary>
		/// Worked out from the shift itself: the standing roster, signups, single-day edits and trades, or for a static
		/// shift (workshift) the crews of its units.
		/// </summary>
		Automatic = 0,

		/// <summary>A supervisor set (approved) the staffing on the Shift Staffing page; it replaces the automatic staffing.</summary>
		Manual = 1
	}

	/// <summary>
	/// The kinds of shift that put people on duty. Assigned and Signup match <see cref="ShiftAssignmentTypes"/>; Workshift
	/// is a static shift (<see cref="Model.Workshift"/>), which schedules units for a set period.
	/// </summary>
	public enum StaffedShiftTypes
	{
		Assigned = 0,
		Signup = 1,
		Workshift = 2
	}

	/// <summary>One person staffing a shift day.</summary>
	public class ShiftDayStaffingPerson
	{
		public string UserId { get; set; }

		/// <summary>The department group (team) whose slot the person fills; for a static shift the unit's station.</summary>
		public int? DepartmentGroupId { get; set; }

		/// <summary>Static shifts: the unit the person crews.</summary>
		public int? UnitId { get; set; }

		/// <summary>Automatic shift staffing: how the roster put the person on the day.</summary>
		public ShiftRosterSources? RosterSource { get; set; }
	}

	/// <summary>
	/// The staffing of one shift occurrence (a shift day, or one day of a static shift) in department-local time: who is
	/// on it and whether a supervisor set that (<see cref="ShiftStaffingSources.Manual"/>) or it comes from the shift
	/// (<see cref="ShiftStaffingSources.Automatic"/>). Built by the shift staffing service; not persisted.
	/// </summary>
	public class ShiftDayStaffing
	{
		public StaffedShiftTypes ShiftType { get; set; }

		public int? ShiftId { get; set; }

		public int? ShiftDayId { get; set; }

		public string WorkshiftId { get; set; }

		public string WorkshiftDayId { get; set; }

		public string Name { get; set; }

		public string Color { get; set; }

		/// <summary>The day the occurrence belongs to (department local, date only).</summary>
		public DateTime Day { get; set; }

		/// <summary>Department-local start of the occurrence.</summary>
		public DateTime Start { get; set; }

		/// <summary>Department-local end of the occurrence.</summary>
		public DateTime End { get; set; }

		public ShiftStaffingSources Source { get; set; }

		/// <summary>Who is staffing the occurrence: the manual staffing when one is set, otherwise <see cref="AutomaticPersonnel"/>.</summary>
		public List<ShiftDayStaffingPerson> Personnel { get; set; } = new List<ShiftDayStaffingPerson>();

		/// <summary>Who the shift itself puts on the occurrence, whether or not a supervisor overrode it.</summary>
		public List<ShiftDayStaffingPerson> AutomaticPersonnel { get; set; } = new List<ShiftDayStaffingPerson>();

		/// <summary>The manual staffing record (ShiftStaffings) in effect, for shift days.</summary>
		public int? ShiftStaffingId { get; set; }

		public string SetByUserId { get; set; }

		/// <summary>When the manual staffing was set (UTC).</summary>
		public DateTime? SetOn { get; set; }

		public string Note { get; set; }

		/// <summary>The occurrence has ended (department-local now is at or past <see cref="End"/>).</summary>
		public bool IsOver { get; set; }

		/// <summary>Department-local now falls inside the occurrence.</summary>
		public bool IsActive { get; set; }

		public double Hours => End > Start ? (End - Start).TotalHours : 0;

		/// <summary>A stable key for the occurrence: "s:{ShiftDayId}" or "w:{WorkshiftDayId}".</summary>
		public string Key => ShiftType == StaffedShiftTypes.Workshift ? $"w:{WorkshiftDayId}" : $"s:{ShiftDayId}";

		/// <summary>People the manual staffing has that the shift does not put on the occurrence. Empty when automatic.</summary>
		public List<string> AddedUserIds()
		{
			if (Source != ShiftStaffingSources.Manual)
				return new List<string>();

			return DistinctUsers(Personnel).Except(DistinctUsers(AutomaticPersonnel), StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>People the shift puts on the occurrence that the manual staffing left off. Empty when automatic.</summary>
		public List<string> RemovedUserIds()
		{
			if (Source != ShiftStaffingSources.Manual)
				return new List<string>();

			return DistinctUsers(AutomaticPersonnel).Except(DistinctUsers(Personnel), StringComparer.OrdinalIgnoreCase).ToList();
		}

		public List<string> UserIds()
		{
			return DistinctUsers(Personnel);
		}

		private static List<string> DistinctUsers(IEnumerable<ShiftDayStaffingPerson> people)
		{
			return (people ?? Enumerable.Empty<ShiftDayStaffingPerson>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.Select(x => x.UserId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	/// <summary>The outcome of saving a shift day's staffing.</summary>
	public class ShiftStaffingSaveResult
	{
		public ShiftStaffing Staffing { get; set; }

		/// <summary>People put on the day's roster so it matches the staffing.</summary>
		public int RosterAdded { get; set; }

		/// <summary>People taken off the day's roster so it matches the staffing.</summary>
		public int RosterRemoved { get; set; }

		/// <summary>Roster changes that could not be made, by user id. The staffing itself is still saved.</summary>
		public List<KeyValuePair<string, ShiftActionErrors>> RosterErrors { get; set; } = new List<KeyValuePair<string, ShiftActionErrors>>();
	}
}
