using System;
using System.Collections;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Shifts
{
	public class ShiftDayResult
	{
		public int ShiftId { get; set; }
		public string ShiftName { get; set; }
		public int ShiftDayId { get; set; }
		public DateTime ShitDay { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public bool SignedUp { get; set; }
		public int ShiftType { get; set; }

		public List<ShiftDaySignupResult> Signups { get; set; }
		public List<ShiftDayGroupNeedsResult> Needs { get; set; } 
	}

	public class ShiftDaySignupResult
	{
		public string UserId { get; set; }
		public List<int> Roles { get; set; }
	}

	public class ShiftDayGroupNeedsResult
	{
		public int GroupId { get; set; }
		public List<ShiftDayGroupRoleNeedsResult> GroupNeeds { get; set; }
	}

	public class ShiftDayGroupRoleNeedsResult
	{
		public int RoleId { get; set; }
		public int Needed { get; set; }
	}
}