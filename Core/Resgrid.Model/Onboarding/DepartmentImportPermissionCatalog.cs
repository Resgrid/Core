using System.Collections.Generic;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Department Import permissions (registry 72-73, wizard plan section 5.1). Both fall back to department
	/// administrators and neither is offered on the Security screen, so no row can ever be saved for them and the claim
	/// is issued to department administrators only: 72 is deliberately not expandable by role, and 73 waits for RMS to own
	/// its enforcement (Phase 3R, decision D9).
	/// </summary>
	public static class DepartmentImportPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageDepartmentImport, PermissionActions.DepartmentAdminsOnly, false,
				"Run Import & Migration: upload, map, approve, commit, roll back, activate and invite", everyoneOffered: false, shownOnSecurityScreen: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordImports, PermissionActions.DepartmentAdminsOnly, false,
				"Import third-party history into Records and delete an import batch", everyoneOffered: false, shownOnSecurityScreen: false)
		};
	}
}
