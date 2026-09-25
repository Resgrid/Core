using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>Shared resource visibility decision used by authorization and request-local administrative previews.</summary>
	public static class ResourceVisibilityPermission
	{
		public static bool Allows(Permission permission, bool departmentAdmin, bool groupAdmin, int? actorGroup,
			int? targetGroup, IEnumerable<int> roleIds, bool adminOfTargetOrAncestor)
		{
			if (permission == null) return true;
			var sameGroup = actorGroup.HasValue && targetGroup.HasValue && actorGroup == targetGroup;
			switch ((PermissionActions)permission.Action)
			{
				case PermissionActions.DepartmentAdminsOnly: return departmentAdmin;
				case PermissionActions.DepartmentAndGroupAdmins:
					return departmentAdmin || groupAdmin && (!permission.LockToGroup || adminOfTargetOrAncestor);
				case PermissionActions.DepartmentAdminsAndSelectRoles:
					if (departmentAdmin) return true;
					if (permission.LockToGroup && !sameGroup || string.IsNullOrWhiteSpace(permission.Data) || roleIds == null) return false;
					// A malformed stored entry denies that entry rather than throwing out of the visibility check.
					var selected = permission.Data.Split(',')
						.Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null)
						.Where(x => x.HasValue).Select(x => x.Value).ToHashSet();
					return roleIds.Any(selected.Contains);
				case PermissionActions.Everyone: return !permission.LockToGroup || departmentAdmin || sameGroup;
				// These legacy resource gates do not implement the RMS action 4; preserve their deny behavior.
				default: return false;
			}
		}
	}
}
