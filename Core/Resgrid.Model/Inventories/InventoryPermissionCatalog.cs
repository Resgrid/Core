using System.Collections.Generic;
namespace Resgrid.Model
{
	public static class InventoryPermissionCatalog
	{
		// The service inherits AdjustInventory for absent transfer/issue rows; the editor resolves those rows using that same fallback.
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.TransferInventory, PermissionActions.DepartmentAdminsOnly, true, "TransferInventory"),
			new RecordPermissionDescriptor(PermissionTypes.IssueInventory, PermissionActions.DepartmentAdminsOnly, true, "IssueInventory"),
			new RecordPermissionDescriptor(PermissionTypes.ManageControlledSubstances, PermissionActions.DepartmentAdminsOnly, false, "ManageControlledSubstances", false)
		};
	}
}
