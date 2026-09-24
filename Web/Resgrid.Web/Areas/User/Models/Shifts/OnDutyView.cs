using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	/// <summary>
	/// Everyone on duty right now across the department's running shift days, grouped by shift and group. Approved
	/// roster entries only: pending signups and trades are not on duty.
	/// </summary>
	public class OnDutyView
	{
		public Department Department { get; set; }
		public DateTime LocalNow { get; set; }
		public List<OnDutyShiftView> Shifts { get; set; } = new List<OnDutyShiftView>();
	}

	public class OnDutyShiftView
	{
		public int ShiftId { get; set; }
		public int ShiftDayId { get; set; }
		public string ShiftName { get; set; }
		public string Color { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public int OpenSlots { get; set; }
		public List<OnDutyGroupView> Groups { get; set; } = new List<OnDutyGroupView>();
	}

	public class OnDutyGroupView
	{
		public int? GroupId { get; set; }
		public string GroupName { get; set; }
		public List<OnDutyPersonView> People { get; set; } = new List<OnDutyPersonView>();
	}

	public class OnDutyPersonView
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Roles { get; set; }
		public ShiftRosterSources Source { get; set; }
		public string TradedFromName { get; set; }
	}
}
