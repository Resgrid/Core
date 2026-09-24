using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	/// <summary>
	/// The canonical page for one shift day: resolved roster, remaining needs per group and role, the caller's own
	/// status and sign-up options, and the single-day roster tools for supervisors.
	/// </summary>
	public class ShiftDayView
	{
		public int ShiftDayId { get; set; }
		public int ShiftId { get; set; }
		public string ShiftName { get; set; }
		public string Color { get; set; }
		public int AssignmentType { get; set; }
		public bool RequireApproval { get; set; }
		public Department Department { get; set; }

		/// <summary>Department-local start and end of this day's shift (an overnight shift ends the next day).</summary>
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		/// <summary>Running right now (department-local).</summary>
		public bool IsActive { get; set; }
		public bool IsOver { get; set; }
		public bool IsFilled { get; set; }
		public int OpenSlots { get; set; }

		/// <summary>Supervises at least one group on the shift, so gets the day's roster tools.</summary>
		public bool CanSupervise { get; set; }

		public List<ShiftDayGroupView> Groups { get; set; } = new List<ShiftDayGroupView>();

		/// <summary>People on the day without a group, or in a group that is not one of the shift's slots.</summary>
		public List<ShiftDayRosterRow> OtherRoster { get; set; } = new List<ShiftDayRosterRow>();

		public List<ShiftDayTradeRow> Trades { get; set; } = new List<ShiftDayTradeRow>();

		public ShiftDayUserStatus MyStatus { get; set; }
		public string MyReviewNote { get; set; }

		/// <summary>A shift with no groups that people sign up for directly.</summary>
		public bool CanSignupWithoutGroup { get; set; }

		public List<ShiftDayOption> AddPersonnelOptions { get; set; } = new List<ShiftDayOption>();
		public List<ShiftDayOption> AddGroupOptions { get; set; } = new List<ShiftDayOption>();

		public bool CanAddPerson => CanSupervise && !IsOver && AddPersonnelOptions.Count > 0 && AddGroupOptions.Count > 0;
	}

	public enum ShiftDayUserStatus
	{
		None = 0,
		OnDuty = 1,
		Pending = 2,
		Removed = 3
	}

	public class ShiftDayGroupView
	{
		public int DepartmentGroupId { get; set; }
		public string Name { get; set; }
		public bool CanManage { get; set; }
		public bool CanSignup { get; set; }
		public List<ShiftDayRoleNeed> Needs { get; set; } = new List<ShiftDayRoleNeed>();
		public List<ShiftDayRosterRow> Roster { get; set; } = new List<ShiftDayRosterRow>();
	}

	public class ShiftDayRoleNeed
	{
		public int PersonnelRoleId { get; set; }
		public string RoleName { get; set; }
		public int Required { get; set; }
		public int Optional { get; set; }

		/// <summary>People still needed for this role (never below zero).</summary>
		public int Remaining { get; set; }
	}

	public class ShiftDayRosterRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int? GroupId { get; set; }
		public string GroupName { get; set; }
		public string Roles { get; set; }
		public ShiftRosterSources Source { get; set; }
		public string TradedFromName { get; set; }
		public bool ApprovalPending { get; set; }
		public int? ShiftSignupId { get; set; }
		public bool IsYou { get; set; }

		/// <summary>The caller supervises this slot: may take the person off the day or review a pending signup.</summary>
		public bool CanManage { get; set; }

		/// <summary>The caller's own signup that they can withdraw from.</summary>
		public bool CanWithdraw { get; set; }

		public bool CanReview => CanManage && ApprovalPending && ShiftSignupId.HasValue && Source != ShiftRosterSources.Trade;
	}

	/// <summary>One roster table on the day page (a group's, or the people outside the shift's groups).</summary>
	public class ShiftDayRosterList
	{
		public int ShiftDayId { get; set; }
		public bool ShowGroup { get; set; }
		public List<ShiftDayRosterRow> Rows { get; set; } = new List<ShiftDayRosterRow>();
	}

	public class ShiftDayTradeRow
	{
		public int ShiftSignupTradeId { get; set; }
		public string FromName { get; set; }
		public string ToName { get; set; }
		public string GroupName { get; set; }
		public DateTime? SwapBackDay { get; set; }
		public string Note { get; set; }
		public string ReviewNote { get; set; }
		public ShiftTradeStatus Status { get; set; }
		public bool CanReview { get; set; }
	}

	public enum ShiftTradeStatus
	{
		Open = 0,
		PendingApproval = 1,
		Complete = 2,
		Denied = 3
	}

	public class ShiftDayOption
	{
		public string Value { get; set; }
		public string Text { get; set; }
	}
}
