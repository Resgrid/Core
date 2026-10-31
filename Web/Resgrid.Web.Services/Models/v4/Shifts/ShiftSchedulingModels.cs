using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Shifts
{
	/// <summary>
	/// Result of a shift signup, trade or roster action
	/// </summary>
	public class ShiftOperationResult : StandardApiResponseV4Base
	{
		/// <summary>Id of the signup or trade that was created or changed</summary>
		public string Id { get; set; }

		/// <summary>The change is waiting for supervisor approval</summary>
		public bool ApprovalPending { get; set; }

		/// <summary>Why the action failed (snake_case code), empty on success</summary>
		public string ErrorCode { get; set; }
	}

	/// <summary>A role requirement on a shift group</summary>
	public class ShiftGroupRoleResultData
	{
		public string RoleId { get; set; }

		public string RoleName { get; set; }

		public int Required { get; set; }
	}

	/// <summary>A group (team) on a shift with its role requirements</summary>
	public class ShiftGroupResultData
	{
		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public List<ShiftGroupRoleResultData> Roles { get; set; } = new List<ShiftGroupRoleResultData>();
	}

	/// <summary>One person on a shift day</summary>
	public class ShiftDayRosterResultData
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public List<int> RoleIds { get; set; } = new List<int>();

		public List<string> Roles { get; set; } = new List<string>();

		/// <summary>0 assigned, 1 signup, 2 added by a supervisor, 3 trade</summary>
		public int Source { get; set; }

		public string ShiftSignupId { get; set; }

		/// <summary>Waiting for supervisor approval; not on duty yet</summary>
		public bool ApprovalPending { get; set; }

		public string TradedFromUserId { get; set; }

		public string TradedFromName { get; set; }

		/// <summary>Static shift (workshift) days: the unit the person crews</summary>
		public string UnitId { get; set; }

		public string UnitName { get; set; }
	}

	/// <summary>A day offered back in exchange for a trade</summary>
	public class ShiftTradeOfferedShiftResultData
	{
		public string ShiftSignupId { get; set; }

		public string ShiftName { get; set; }

		public DateTime ShiftDay { get; set; }
	}

	/// <summary>Someone asked to take a trade</summary>
	public class ShiftTradeUserResultData
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public bool Declined { get; set; }

		public bool Offered { get; set; }

		/// <summary>Their note when applying or declining; empty unless it is the caller's own or the caller may decide the trade</summary>
		public string Reason { get; set; }

		public List<ShiftTradeOfferedShiftResultData> OfferedShifts { get; set; } = new List<ShiftTradeOfferedShiftResultData>();

		/// <summary>When they applied or answered (UTC), when known</summary>
		public DateTime? RespondedOn { get; set; }

		public string GroupName { get; set; }

		public List<string> Roles { get; set; } = new List<string>();

		/// <summary>Detail only (GetShiftTrade): still meets the slot's requirements</summary>
		public bool IsEligible { get; set; }

		/// <summary>Detail only: why not, as an error code (already_on_roster, not_eligible), when not eligible</summary>
		public string IneligibleReason { get; set; }

		/// <summary>Detail only: current certifications ("Name" or "Name (expires yyyy-MM-dd)")</summary>
		public List<string> Certifications { get; set; } = new List<string>();

		/// <summary>Detail only: shift and static shift days they work in the 7 days either side of the trade day</summary>
		public int NearbyShiftCount { get; set; }

		/// <summary>Detail only: hours of those days</summary>
		public double NearbyShiftHours { get; set; }

		/// <summary>Detail only: they are already working another shift that overlaps the traded day</summary>
		public bool HasConflict { get; set; }

		/// <summary>Detail only: the shift that overlaps, when they have one</summary>
		public string ConflictName { get; set; }
	}

	/// <summary>A shift trade</summary>
	public class ShiftTradeResultData
	{
		public string ShiftSignupTradeId { get; set; }

		/// <summary>0 outgoing (caller asked), 1 incoming (caller was asked)</summary>
		public int Direction { get; set; }

		/// <summary>0 open, 1 pending approval, 2 completed, 3 denied</summary>
		public int Status { get; set; }

		/// <summary>The caller's own state as an invited user (ShiftSignupTradeStates)</summary>
		public int MyState { get; set; }

		public string SourceShiftSignupId { get; set; }

		public string SourceUserId { get; set; }

		public string SourceUserName { get; set; }

		public string ShiftId { get; set; }

		public string ShiftName { get; set; }

		public string ShiftDayId { get; set; }

		public DateTime ShiftDay { get; set; }

		public DateTime Start { get; set; }

		public DateTime End { get; set; }

		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public string Note { get; set; }

		public List<ShiftTradeUserResultData> Users { get; set; } = new List<ShiftTradeUserResultData>();

		public string AcceptedUserId { get; set; }

		public string AcceptedUserName { get; set; }

		public string TargetShiftSignupId { get; set; }

		public DateTime? TargetShiftDay { get; set; }

		/// <summary>The approver's note; empty for a caller who is neither part of the trade nor able to decide it</summary>
		public string ReviewNote { get; set; }

		public string ReviewedByName { get; set; }

		public bool RequireApproval { get; set; }

		/// <summary>The caller can approve or deny it now</summary>
		public bool CanReview { get; set; }

		/// <summary>Open to everyone eligible (who apply) rather than only the people asked</summary>
		public bool IsOpen { get; set; }

		/// <summary>0 assigned (static) shift, 1 signup shift, 2 static shift (workshift) day</summary>
		public int ShiftType { get; set; }

		public string WorkshiftId { get; set; }

		public string WorkshiftDayId { get; set; }

		public string UnitId { get; set; }

		public string UnitName { get; set; }

		/// <summary>When it was requested (UTC), when known</summary>
		public DateTime? RequestedOn { get; set; }

		/// <summary>Offers made and not withdrawn</summary>
		public int ApplicantCount { get; set; }

		/// <summary>The caller can apply to take the slot now</summary>
		public bool CanApply { get; set; }

		/// <summary>The caller meets the slot's requirements (open requests; computed for GetOpenShiftTrades and GetShiftTrade)</summary>
		public bool IsEligible { get; set; }

		/// <summary>Why the caller cannot apply, as an error code, when they cannot</summary>
		public string IneligibleReason { get; set; }

		/// <summary>The caller can pick which applicant takes the slot</summary>
		public bool CanPick { get; set; }

		/// <summary>The caller picks as an approver, so the pick is also the approval (a requester's pick may still wait for one)</summary>
		public bool PickIsApproval { get; set; }

		/// <summary>The caller can cancel it</summary>
		public bool CanCancel { get; set; }

		/// <summary>The traded day has ended</summary>
		public bool IsOver { get; set; }
	}

	public class ShiftTradeResult : StandardApiResponseV4Base
	{
		public ShiftTradeResultData Data { get; set; }
	}

	public class RequestWorkshiftTradeInput
	{
		public string WorkshiftDayId { get; set; }

		public string Note { get; set; }
	}

	/// <summary>Trades for the caller</summary>
	public class ShiftTradesResult : StandardApiResponseV4Base
	{
		public List<ShiftTradeResultData> Data { get; set; } = new List<ShiftTradeResultData>();
	}

	/// <summary>A signup waiting for approval</summary>
	public class PendingShiftSignupResultData
	{
		public string ShiftSignupId { get; set; }

		public string UserId { get; set; }

		public string UserName { get; set; }

		public string ShiftId { get; set; }

		public string ShiftName { get; set; }

		public string ShiftDayId { get; set; }

		public DateTime ShiftDay { get; set; }

		public DateTime Start { get; set; }

		public DateTime End { get; set; }

		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public DateTime SignupTimestamp { get; set; }

		public List<string> Roles { get; set; } = new List<string>();
	}

	/// <summary>What the caller can approve</summary>
	public class PendingShiftApprovalsResultData
	{
		public bool IsSupervisor { get; set; }

		public List<PendingShiftSignupResultData> Signups { get; set; } = new List<PendingShiftSignupResultData>();

		public List<ShiftTradeResultData> Trades { get; set; } = new List<ShiftTradeResultData>();
	}

	/// <summary>Pending shift approvals</summary>
	public class PendingShiftApprovalsResult : StandardApiResponseV4Base
	{
		public PendingShiftApprovalsResultData Data { get; set; } = new PendingShiftApprovalsResultData();
	}

	/// <summary>A person who can be picked for a shift day</summary>
	public class ShiftPersonOptionResultData
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public List<string> Roles { get; set; } = new List<string>();

		public List<int> RoleIds { get; set; } = new List<int>();
	}

	/// <summary>People who can be picked for a shift day</summary>
	public class ShiftPersonOptionsResult : StandardApiResponseV4Base
	{
		public List<ShiftPersonOptionResultData> Data { get; set; } = new List<ShiftPersonOptionResultData>();
	}

	/// <summary>Someone on duty right now</summary>
	public class OnDutyPersonResultData
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public string ShiftId { get; set; }

		public string ShiftName { get; set; }

		public string ShiftDayId { get; set; }

		public DateTime Start { get; set; }

		public DateTime End { get; set; }

		public string GroupId { get; set; }

		public string GroupName { get; set; }

		public List<string> Roles { get; set; } = new List<string>();

		public int Source { get; set; }
	}

	/// <summary>Everyone on duty right now</summary>
	public class OnDutyPersonnelResult : StandardApiResponseV4Base
	{
		public List<OnDutyPersonResultData> Data { get; set; } = new List<OnDutyPersonResultData>();
	}

	public class WithdrawShiftSignupInput
	{
		public int ShiftSignupId { get; set; }
	}

	public class RequestShiftTradeInput
	{
		public int ShiftDayId { get; set; }

		public List<string> UserIds { get; set; }

		public string Note { get; set; }
	}

	public class RespondToShiftTradeInput
	{
		public int ShiftSignupTradeId { get; set; }

		public bool Accept { get; set; }

		public string Note { get; set; }

		public List<int> OfferedShiftSignupIds { get; set; }
	}

	public class FinishShiftTradeInput
	{
		public int ShiftSignupTradeId { get; set; }

		public string AcceptedUserId { get; set; }

		public int? TargetShiftSignupId { get; set; }
	}

	public class CancelShiftTradeInput
	{
		public int ShiftSignupTradeId { get; set; }
	}

	public class ReviewShiftSignupInput
	{
		public int ShiftSignupId { get; set; }

		public bool Approve { get; set; }

		public string Note { get; set; }
	}

	public class ReviewShiftTradeInput
	{
		public int ShiftSignupTradeId { get; set; }

		public bool Approve { get; set; }

		public string Note { get; set; }
	}

	public class AssignToShiftDayInput
	{
		public int ShiftDayId { get; set; }

		public string UserId { get; set; }

		public int GroupId { get; set; }
	}

	public class RemoveFromShiftDayInput
	{
		public int ShiftDayId { get; set; }

		public string UserId { get; set; }

		public string Note { get; set; }
	}
}
