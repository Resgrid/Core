using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Who may take the slot a trade gives away: active department members who are not already on that day and hold the
	/// roles the slot needs (the roles its group requires that the person trading out holds). Static shift (workshift)
	/// days have no role requirements.
	/// </summary>
	public class ShiftTradeEligibility
	{
		public string SourceUserId { get; set; }

		public HashSet<string> MemberIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> OnDayUserIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public List<int> RequiredRoleIds { get; set; } = new List<int>();

		public Dictionary<string, List<int>> RoleIdsByUser { get; set; } = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

		public bool IsEligible(string userId)
		{
			return Check(userId) == ShiftActionErrors.None;
		}

		/// <summary>Why the user cannot take the slot, or <see cref="ShiftActionErrors.None"/> when they can.</summary>
		public ShiftActionErrors Check(string userId)
		{
			if (String.IsNullOrWhiteSpace(userId) || String.Equals(userId, SourceUserId, StringComparison.OrdinalIgnoreCase))
				return ShiftActionErrors.NotAllowed;

			if (!MemberIds.Contains(userId))
				return ShiftActionErrors.NotAllowed;

			if (OnDayUserIds.Contains(userId))
				return ShiftActionErrors.AlreadyOnRoster;

			if (RequiredRoleIds.Any())
			{
				var roles = RoleIdsByUser.TryGetValue(userId, out var userRoles) ? userRoles : new List<int>();

				if (!RequiredRoleIds.All(roles.Contains))
					return ShiftActionErrors.NotEligible;
			}

			return ShiftActionErrors.None;
		}

		public List<string> EligibleUserIds()
		{
			return MemberIds.Where(IsEligible).ToList();
		}
	}

	/// <summary>
	/// What a user may do with a shift trade under the department's <see cref="ShiftTradeApproverSettings"/>.
	/// </summary>
	public class ShiftTradeAuthority
	{
		public string UserId { get; set; }

		public ShiftManagementScope Scope { get; set; } = ShiftManagementScope.None();

		public ShiftTradeApproverSettings Settings { get; set; } = ShiftTradeApproverSettings.Default();

		public List<int> RoleIds { get; set; } = new List<int>();

		/// <summary>An approver for any trade in the department (not counting group admin rights).</summary>
		public bool IsDepartmentApprover => (Scope?.AllGroups ?? false) || (Settings?.IsRoleApprover(RoleIds) ?? false);

		/// <summary>May see the approvals queue: supervises a group, or holds an approver role.</summary>
		public bool HasApprovals => (Scope?.IsSupervisor ?? false) || (Settings?.IsRoleApprover(RoleIds) ?? false);

		/// <summary>
		/// Decides trades for the slot as an approver (department admin or department-wide shift manager, an admin of the
		/// slot's group when group admins may decide, or a member of an approver role), apart from being the requester.
		/// </summary>
		public bool IsApprover(Shift shift, int? departmentGroupId)
		{
			if (IsDepartmentApprover)
				return true;

			if (Settings?.GroupAdmins != true || Scope == null)
				return false;

			// A static shift day has no Shift; its slot belongs to the unit's station group.
			return shift != null ? Scope.CanManageShiftGroup(shift, departmentGroupId) : Scope.CanManageGroup(departmentGroupId);
		}

		public bool IsRequester(ShiftSignupTrade trade)
		{
			return trade != null && String.Equals(trade.GetSourceUserId(), UserId, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>May pick which applicant takes the slot.</summary>
		public bool CanPick(ShiftSignupTrade trade, Shift shift)
		{
			if (trade == null || !trade.IsOpenForOffers())
				return false;

			return IsApprover(shift, trade.GetDepartmentGroupId()) || (Settings?.Requester == true && IsRequester(trade));
		}

		/// <summary>May approve or deny a pick that is waiting for approval.</summary>
		public bool CanReview(ShiftSignupTrade trade, Shift shift)
		{
			return trade != null && trade.ApprovalPending && !trade.Denied && IsApprover(shift, trade.GetDepartmentGroupId());
		}

		/// <summary>May withdraw a trade that has not taken effect: the requester, or anyone who supervises its slot.</summary>
		public bool CanCancel(ShiftSignupTrade trade, Shift shift)
		{
			if (trade == null || trade.IsTradeComplete())
				return false;

			return IsRequester(trade) || IsSupervisor(trade, shift);
		}

		/// <summary>Cancels as a supervisor rather than as the person trading out.</summary>
		public bool IsSupervisor(ShiftSignupTrade trade, Shift shift)
		{
			if (trade == null || Scope == null)
				return false;

			var groupId = trade.GetDepartmentGroupId();
			var managesSlot = shift != null ? Scope.CanManageShiftGroup(shift, groupId) : Scope.CanManageGroup(groupId) || Scope.AllGroups;

			return managesSlot || IsApprover(shift, groupId);
		}
	}
}
