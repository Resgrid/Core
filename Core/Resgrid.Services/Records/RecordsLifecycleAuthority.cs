using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Who may close out a Record or incident report that someone else wrote. DeleteRecord and FinalizeRecords
	/// default to Everyone, so the grant alone let any member void, cancel or finalize any Record they could see.
	/// The floor is the legacy Logs rule the grant was activated from (CanUserDeleteWorkLogAsync: a department
	/// administrator or the member who logged it), widened only to the people the lifecycle already names on the row.
	/// Operational Records and incident reports share the id space and the group-scope table, so one rule serves both.
	/// </summary>
	public static class RecordsLifecycleAuthority
	{
		/// <summary>
		/// Void and cancel (DeleteRecord): the author, the current owner, the assigned reviewer or approver, a department
		/// administrator, or the group administrator of a group the Record belongs to.
		/// </summary>
		public static async Task<bool> CanVoidOrCancelAsync(IRecordsAuthorizationService authorization, IDepartmentGroupsService groups, IRmsRecordGroupScopesRepository scopes,
			string userId, int departmentId, string recordId, int? stationGroupId, string authorUserId, string ownerUserId, string reviewerUserId, string approverUserId)
		{
			if (string.IsNullOrWhiteSpace(userId)) return false;
			if (IsNamed(userId, authorUserId, ownerUserId, reviewerUserId, approverUserId)) return true;
			if (await authorization.IsDepartmentAdminAsync(userId, departmentId)) return true;
			return await IsGroupAdminOfRecordAsync(groups, scopes, userId, departmentId, recordId, stationGroupId);
		}

		/// <summary>
		/// Finalize, and the correction signature that follows a rejection: the author, the current owner, the assigned
		/// reviewer or approver, a department administrator, a ReviewRecords holder (the reviewer who finalizes out of
		/// review), an ApproveRecords holder once the Record is approved, and an AmendRecords holder for an amendment
		/// or a correction (both write an amended revision).
		/// </summary>
		public static async Task<bool> CanFinalizeAsync(IRecordsAuthorizationService authorization, string userId, int departmentId, RmsRecordState from, bool amendment,
			string authorUserId, string ownerUserId, string reviewerUserId, string approverUserId)
		{
			if (string.IsNullOrWhiteSpace(userId)) return false;
			if (IsNamed(userId, authorUserId, ownerUserId, reviewerUserId, approverUserId)) return true;
			if (await authorization.IsDepartmentAdminAsync(userId, departmentId)) return true;
			if (await authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ReviewRecords)) return true;
			if (from == RmsRecordState.Approved && await authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ApproveRecords)) return true;
			return amendment && await authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.AmendRecords);
		}

		private static bool IsNamed(string userId, params string[] named)
			=> named.Any(n => string.Equals(n, userId, StringComparison.Ordinal));

		private static async Task<bool> IsGroupAdminOfRecordAsync(IDepartmentGroupsService groups, IRmsRecordGroupScopesRepository scopes, string userId, int departmentId, string recordId, int? stationGroupId)
		{
			var group = await groups.GetGroupForUserAsync(userId, departmentId);
			if (group == null || group.DepartmentId != departmentId || !group.IsUserGroupAdmin(userId)) return false;
			if (stationGroupId == group.DepartmentGroupId) return true;
			var scope = await scopes.GetForRecordAsync(departmentId, recordId);
			return scope != null && scope.Any(s => s.DepartmentGroupId == group.DepartmentGroupId);
		}
	}
}
