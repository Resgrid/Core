using System.Collections.Generic;

namespace Resgrid.Model
{
	public static class WorkOrderPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageWorkOrders, PermissionActions.DepartmentAdminsOnly, false, "ManageWorkOrdersNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewAllWorkOrders, PermissionActions.DepartmentAndGroupAdmins, true, "ViewAllWorkOrdersNote")
		};
	}
}
