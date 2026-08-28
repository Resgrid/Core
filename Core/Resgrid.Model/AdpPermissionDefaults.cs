using System;

namespace Resgrid.Model
{
	/// <summary>
	/// No-row defaults for the Advanced Data Protection permissions (PermissionTypes 31-39).
	///
	/// Resgrid's permission convention is "a missing Permission row means allowed"
	/// (IPermissionsService.IsUserAllowed returns true on null) — wide open. That is the WRONG
	/// default for protected data, so every ADP authorization check MUST resolve a missing row
	/// through this map instead of the null-allow convention, and the Security &amp; Permissions
	/// admin page preselects the same values so what admins see matches what is enforced.
	///
	/// Rationale per value:
	/// - View/Edit protected CALL data default to Everyone: responding personnel must be able to
	///   read the nature and address of a dispatch or the response function breaks. The step-up
	///   MFA grant still gates every reveal — "Everyone" here means every member who passes MFA,
	///   never anonymous width.
	/// - Protected PERSONNEL and CONTACT data (PII: employee IDs, emergency contacts, government
	///   IDs) default to department admins.
	/// - Protected OPERATIONAL data (logs, forms, IC content) defaults to department and group
	///   admins — command staff read it, the general roster does not, and departments widen it
	///   per role as needed.
	/// - Export, egress configuration, break-glass, and ADP settings management default to
	///   department admins. Break-glass additionally requires the department's policy to enable
	///   it at all (plan section 12) — the permission alone is never sufficient.
	/// </summary>
	public static class AdpPermissionDefaults
	{
		public static PermissionActions For(PermissionTypes type)
		{
			switch (type)
			{
				case PermissionTypes.ViewProtectedCallData:
				case PermissionTypes.EditProtectedCallData:
					return PermissionActions.Everyone;

				case PermissionTypes.ViewProtectedOperationalData:
					return PermissionActions.DepartmentAndGroupAdmins;

				case PermissionTypes.ManageDepartmentDataProtection:
				case PermissionTypes.ViewProtectedPersonnelData:
				case PermissionTypes.ViewProtectedContactData:
				case PermissionTypes.ExportProtectedData:
				case PermissionTypes.ConfigureProtectedDataEgress:
				case PermissionTypes.BreakGlassProtectedData:
					return PermissionActions.DepartmentAdminsOnly;

				default:
					throw new ArgumentOutOfRangeException(nameof(type),
						$"{type} is not an Advanced Data Protection permission; use the standard permission evaluation.");
			}
		}
	}
}
