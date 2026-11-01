using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// shifts.coverage (plan §7.6): the next days' shift and static shift staffing, approved (manual) or automatic, with
	/// unstaffed days flagged. Supervisors only, like the Shift Staffing page (shifts they supervise; static shifts for
	/// department-wide supervisors).
	/// </summary>
	public sealed class ShiftCoverageWidgetData
	{
		/// <summary>False when the viewer supervises no group (the staffing page refuses them).</summary>
		public bool CanView { get; set; }

		public int Days { get; set; }

		/// <summary>Department-local first day, "yyyy-MM-dd".</summary>
		public string StartDay { get; set; }

		public int GapCount { get; set; }

		public List<ShiftCoverageItem> Items { get; set; } = new List<ShiftCoverageItem>();

		public string StaffingUrl { get; set; }
	}

	public sealed class ShiftCoverageItem
	{
		/// <summary>"s:{shiftDayId}" or "w:{workshiftDayId}".</summary>
		public string Key { get; set; }

		/// <summary>"shift" or "workshift" (static shift).</summary>
		public string Kind { get; set; }

		public int? ShiftId { get; set; }

		public int? ShiftDayId { get; set; }

		public string WorkshiftDayId { get; set; }

		public string Name { get; set; }

		public string Color { get; set; }

		/// <summary>Department-local date the occurrence belongs to, "yyyy-MM-dd".</summary>
		public string Day { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>"manual" (approved by a supervisor) or "automatic" (from the roster).</summary>
		public string Source { get; set; }

		/// <summary>People on the effective staffing.</summary>
		public int People { get; set; }

		/// <summary>People the roster would put on it.</summary>
		public int AutomaticPeople { get; set; }

		public int AddedCount { get; set; }

		public int RemovedCount { get; set; }

		/// <summary>Nobody is staffed.</summary>
		public bool IsGap { get; set; }

		public bool IsActive { get; set; }

		/// <summary>Already ended (today's earlier occurrences).</summary>
		public bool IsOver { get; set; }
	}
}
