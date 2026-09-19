using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable certification permissions (Workforce &amp; Business Operations plan, D8; registry 42–44).
	/// The single source of the no-row defaults: all three fall back to department administrators; a member's own
	/// records are always visible without any of them. Unit certification records ride the same permissions.
	/// </summary>
	public static class CertificationPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageCertifications, PermissionActions.DepartmentAdminsOnly, false, "ManageCertificationsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewCertifications, PermissionActions.DepartmentAdminsOnly, false, "ViewCertificationsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageCertificationSetup, PermissionActions.DepartmentAdminsOnly, false, "ManageCertificationSetupNote", false)
		};
	}
}
