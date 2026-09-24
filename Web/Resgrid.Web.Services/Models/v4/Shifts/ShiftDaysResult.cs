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

		/// <summary>Shift color</summary>
		public string Color { get; set; }

		/// <summary>Signups and trades on this shift need supervisor approval</summary>
		public bool RequireApproval { get; set; }

		/// <summary>Every role requirement is met by people on duty (pending approvals do not count)</summary>
		public bool Filled { get; set; }

		/// <summary>People still needed across all groups and roles</summary>
		public int OpenSlots { get; set; }

		/// <summary>The shift day is running right now (department local time)</summary>
		public bool IsActive { get; set; }

		/// <summary>The caller supervises at least one group on this shift</summary>
		public bool CanManage { get; set; }

		/// <summary>The caller can sign up for a slot on this day</summary>
		public bool CanSignup { get; set; }

		/// <summary>The caller's status on this day: 0 none, 1 on roster, 2 pending approval, 3 denied or removed</summary>
		public int MyStatus { get; set; }

		/// <summary>The caller's own signup for the day, when they have one</summary>
		public string MySignupId { get; set; }

		/// <summary>The group the caller is on for the day</summary>
		public string MyGroupId { get; set; }

		/// <summary>An open or pending trade on the caller's slot for the day</summary>
		public string MyTradeId { get; set; }

		/// <summary>Who is on the day after assignments, signups, single-day edits and trades</summary>
		public List<ShiftDayRosterResultData> Roster { get; set; }

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

		/// <summary>Group the signup is for</summary>
		public string GroupId { get; set; }

		/// <summary>Signup identifier</summary>
		public string ShiftSignupId { get; set; }

		/// <summary>Waiting for supervisor approval</summary>
		public bool ApprovalPending { get; set; }
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

		/// <summary>The caller supervises this group</summary>
		public bool CanManage { get; set; }
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
