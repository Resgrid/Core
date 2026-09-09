using System.Collections.Generic;

namespace Resgrid.Model
{
	public static class ChecklistPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageChecklists, PermissionActions.DepartmentAdminsOnly, false, "Create, edit, publish and retire checklists", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewChecklistResults, PermissionActions.DepartmentAndGroupAdmins, true, "View other members' checklist results")
		};
	}
}
