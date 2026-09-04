namespace Resgrid.Model
{
	public enum PermissionActions
	{
		DepartmentAdminsOnly = 0,
		DepartmentAndGroupAdmins = 1,
		DepartmentAdminsAndSelectRoles = 2,
		Everyone = 3,

		/// <summary>
		/// Department admins, group admins, AND the selected personnel roles. Added with RMS (registry
		/// section 4.4) because <see cref="DepartmentAdminsAndSelectRoles"/> excludes group admins. Appending
		/// is safe: consumers that do not recognise the value fall through to deny (fail closed). It is not
		/// yet offered in the generic Permissions dropdown because the pre-RMS claim families do not
		/// evaluate it; selecting it there would silently revoke access until they do.
		/// </summary>
		DepartmentAndGroupAdminsAndSelectRoles = 4
	}
}