using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Shifts
{
	/// <summary>
	/// Shift days for a department
	/// </summary>
	public class ShiftDaysResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ShiftDayResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ShiftDaysResult()
		{
			Data = new List<ShiftDayResultData>();
		}
	}

	/// <summary>
	/// Shift day data
	/// </summary>
	public class ShiftDayResultData
	{
		/// <summary>
		/// Identifier for the parent shift
		/// </summary>
		public string ShiftId { get; set; }

		/// <summary>
		/// Name of the shift
		/// </summary>
		public string ShiftName { get; set; }

		/// <summary>
		/// Shift Day Identifier (this object)
		/// </summary>
		public string ShiftDayId { get; set; }

		/// <summary>
		/// DateTime of the shift day
		/// </summary>
		public DateTime ShiftDay { get; set; }

		/// <summary>
		/// When did the shift day start
		/// </summary>
		public DateTime Start { get; set; }

		/// <summary>
		/// When does the shift day end
		/// </summary>
		public DateTime End { get; set; }

		/// <summary>
		/// Are you signed up to this shift
		/// </summary>
		public bool SignedUp { get; set; }

		/// <summary>
		/// Type of this parent shift
		/// </summary>
		public int ShiftType { get; set; }

		/// <summary>
		/// Signups for the shift (this may be null)
		/// </summary>
		public List<ShiftDaySignupResultData> Signups { get; set; }

		/// <summary>
		/// What does this shift day need (this may be null)
		/// </summary>
		public List<ShiftDayGroupNeedsResultData> Needs { get; set; }
	}

	/// <summary>
	/// Shift Day Signup result data
	/// </summary>
	public class ShiftDaySignupResultData
	{
		/// <summary>
		/// User Id of the user who signed up
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Users Name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Role ids this user fills
		/// </summary>
		public List<int> Roles { get; set; }
	}

	/// <summary>
	/// Shift day group needs
	/// </summary>
	public class ShiftDayGroupNeedsResultData
	{
		/// <summary>
		/// Group Identifier
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Group Name
		/// </summary>
		public string GroupName { get; set; }

		/// <summary>
		/// Role needs of the group
		/// </summary>
		public List<ShiftDayGroupRoleNeedsResultData> GroupNeeds { get; set; }
	}

	/// <summary>
	/// Roles needs for a shift day group
	/// </summary>
	public class ShiftDayGroupRoleNeedsResultData
	{
		/// <summary>
		/// Role Identifier
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		/// Role Name
		/// </summary>
		public string RoleName { get; set; }

		/// <summary>
		/// Number of that role that is needed
		/// </summary>
		public int Needed { get; set; }
	}
}
