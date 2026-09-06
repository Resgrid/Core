using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Per-Record visibility (RMS plan section 5.7.1). Group visibility narrows; it never grants: a user
	/// without Record_View gains nothing from being in the right group, and department administrators see
	/// everything, matching every other LockToGroup permission. Follows the CanUserViewPersonAsync /
	/// CanUserViewUnitAsync exemplars: read the ViewGroupRecords row, branch on PermissionActions, honor
	/// LockToGroup, fail closed on evaluation error.
	/// </summary>
	public interface IRecordsAuthorizationService
	{
		Task<bool> IsActiveMemberAsync(string userId, int departmentId);
		Task<bool> IsDepartmentAdminAsync(string userId, int departmentId);
		Task<bool> HasPermissionAsync(string userId, int departmentId, PermissionTypes permissionType);
		Task<bool> CanReadSourceCallAsync(string userId, int departmentId, Call call);
		Task<bool> CanCreateSourceCallAsync(string userId, int departmentId);
		Task<bool> CanUseSourceInventoryAsync(string userId, int departmentId, int? groupId = null);

		/// <summary>Whether cross-group scoping is in force for the department (setting 75 GroupScoped and ViewGroupRecords LockToGroup).</summary>
		Task<bool> IsGroupScopedAsync(int departmentId);

		/// <summary>
		/// The group ids whose Records the user may see, or null when the user sees the whole department
		/// (department-wide mode, department admin, or an unlocked ViewGroupRecords row). Used as the join
		/// input for list, search and report queries so visibility is a join, not a per-row callback.
		/// </summary>
		Task<List<int>> GetVisibleGroupIdsAsync(string userId, int departmentId);

		/// <summary>Opaque fingerprint of current membership, roles, group and permission policy for local-cache invalidation. Null means read access cannot be established.</summary>
		Task<string> GetReadScopeStampAsync(string userId, int departmentId);

		/// <summary>Per-Record check applied on every detail, attachment and deep-link read.</summary>
		Task<bool> CanUserViewRecordAsync(string userId, string recordId, int departmentId);

		/// <summary>
		/// Per-Record check for a system principal reading under a configured grant (Identifier Allocation
		/// Registry section 4.4). A system principal is nobody: none of the always-visible author/owner/
		/// reviewer/participant cases apply to it, so a non-department-wide grant sees exactly the groups it
		/// names and nothing else.
		/// </summary>
		Task<bool> CanSystemPrincipalViewRecordAsync(SystemPrincipalRecordGrant grant, string recordId);

		/// <summary>What GroupScoped would hide, by group, before an administrator confirms it (plan 5.7.1 "Turning it on").</summary>
		Task<RecordsGroupScopePreview> PreviewGroupScopingAsync(int departmentId);
	}
}
