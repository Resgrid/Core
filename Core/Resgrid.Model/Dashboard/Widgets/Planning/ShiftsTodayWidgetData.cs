using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>shifts.today (plan §7.6): today's shift days (department-local date) with staffing against the requirement and who is on duty by group.</summary>
	public sealed class ShiftsTodayWidgetData
	{
		/// <summary>Department-local date, "yyyy-MM-dd".</summary>
		public string Day { get; set; }

		public List<ShiftsTodayItem> Shifts { get; set; } = new List<ShiftsTodayItem>();
	}

	public sealed class ShiftsTodayItem
	{
		public int ShiftId { get; set; }

		public int ShiftDayId { get; set; }

		public string Name { get; set; }

		public string Color { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>Running now (department-local).</summary>
		public bool IsActive { get; set; }

		public bool IsOver { get; set; }

		/// <summary>Every group/role requirement is met.</summary>
		public bool IsFilled { get; set; }

		/// <summary>Unfilled positions across the day's requirements.</summary>
		public int OpenSlots { get; set; }

		/// <summary>Distinct people on the day's roster who are on duty (approved; not traded away).</summary>
		public int OnDutyCount { get; set; }

		/// <summary>The shift's groups (even with nobody on them), then other groups people were placed in, then "no group" (GroupId null).</summary>
		public List<ShiftsTodayGroup> Groups { get; set; } = new List<ShiftsTodayGroup>();

		public string Url { get; set; }
	}

	public sealed class ShiftsTodayGroup
	{
		public int? GroupId { get; set; }

		/// <summary>Empty for "no group" (the client labels it).</summary>
		public string GroupName { get; set; }

		public int OnDutyCount { get; set; }
	}
}
