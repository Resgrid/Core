using Microsoft.Extensions.Localization;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>
	/// Display text for the permission action dropdowns on the Permissions screen, shared by the fixed rows
	/// built in <c>SecurityController.Index</c> and the generated <see cref="RecordsPermissionRows"/>. The
	/// permission notes name these options, so both must come from the same Security resources.
	/// </summary>
	public class PermissionOptionLabels
	{
		public const string EveryoneKey = "PermOptionEveryone";
		public const string DepartmentAdminsKey = "PermOptionDepartmentAdmins";
		public const string DepartmentAndGroupAdminsKey = "PermOptionDepartmentAndGroupAdmins";
		public const string DepartmentAdminsAndSelectRolesKey = "PermOptionDepartmentAdminsAndSelectRoles";
		public const string DepartmentGroupAdminsAndSelectRolesKey = "PermOptionDepartmentGroupAdminsAndSelectRoles";

		public static readonly string[] Keys =
		{
			EveryoneKey, DepartmentAdminsKey, DepartmentAndGroupAdminsKey, DepartmentAdminsAndSelectRolesKey, DepartmentGroupAdminsAndSelectRolesKey
		};

		/// <summary>Used when no localizer is available (tests and other non-request callers).</summary>
		public static readonly PermissionOptionLabels English = new PermissionOptionLabels
		{
			Everyone = "Everyone",
			DepartmentAdmins = "Department Admins",
			DepartmentAndGroupAdmins = "Department and Group Admins",
			DepartmentAdminsAndSelectRoles = "Department Admins and Select Roles",
			DepartmentGroupAdminsAndSelectRoles = "Department, Group Admins and Select Roles"
		};

		/// <summary>PermissionActions 3.</summary>
		public string Everyone { get; set; }
		/// <summary>PermissionActions 0.</summary>
		public string DepartmentAdmins { get; set; }
		/// <summary>PermissionActions 1.</summary>
		public string DepartmentAndGroupAdmins { get; set; }
		/// <summary>PermissionActions 2.</summary>
		public string DepartmentAdminsAndSelectRoles { get; set; }
		/// <summary>PermissionActions 4, offered on Records rows only.</summary>
		public string DepartmentGroupAdminsAndSelectRoles { get; set; }

		public static PermissionOptionLabels From(IStringLocalizer localizer)
		{
			return new PermissionOptionLabels
			{
				Everyone = localizer[EveryoneKey].Value,
				DepartmentAdmins = localizer[DepartmentAdminsKey].Value,
				DepartmentAndGroupAdmins = localizer[DepartmentAndGroupAdminsKey].Value,
				DepartmentAdminsAndSelectRoles = localizer[DepartmentAdminsAndSelectRolesKey].Value,
				DepartmentGroupAdminsAndSelectRoles = localizer[DepartmentGroupAdminsAndSelectRolesKey].Value
			};
		}
	}
}
