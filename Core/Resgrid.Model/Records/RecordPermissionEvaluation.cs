using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The PermissionActions ladder as one function, including value 4
	/// (DepartmentAndGroupAdminsAndSelectRoles). Department administrators pass every value, an
	/// unrecognised future value denies (fail closed), and a malformed role CSV is treated as empty.
	/// ClaimsLogic (login-time claims) and RecordsAuthorizationService (per-Record checks) both call this
	/// so the two can never disagree.
	/// </summary>
	public static class RecordPermissionEvaluation
	{
		public static bool IsSatisfied(int action, string roleIdCsv, bool isAdmin, bool isGroupAdmin, IEnumerable<PersonnelRole> roles)
		{
			if (isAdmin)
				return true;

			switch ((PermissionActions)action)
			{
				case PermissionActions.DepartmentAdminsOnly:
					return false;
				case PermissionActions.DepartmentAndGroupAdmins:
					return isGroupAdmin;
				case PermissionActions.DepartmentAdminsAndSelectRoles:
					return HasSelectedRole(roleIdCsv, roles);
				case PermissionActions.Everyone:
					return true;
				case PermissionActions.DepartmentAndGroupAdminsAndSelectRoles:
					return isGroupAdmin || HasSelectedRole(roleIdCsv, roles);
				default:
					return false;
			}
		}

		public static bool HasSelectedRole(string roleIdCsv, IEnumerable<PersonnelRole> roles)
		{
			if (String.IsNullOrWhiteSpace(roleIdCsv) || roles == null)
				return false;

			var roleList = roles as IList<PersonnelRole> ?? roles.ToList();
			if (roleList.Count == 0)
				return false;

			foreach (var part in roleIdCsv.Split(','))
			{
				if (int.TryParse(part.Trim(), out var roleId) && roleList.Any(r => r.PersonnelRoleId == roleId))
					return true;
			}

			return false;
		}
	}
}
