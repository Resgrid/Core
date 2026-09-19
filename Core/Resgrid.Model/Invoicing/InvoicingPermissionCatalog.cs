using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable invoicing permissions (Workforce &amp; Business Operations plan, B6; registry 40–41).
	/// The single source of the no-row defaults: both fall back to department administrators. Joins the
	/// ClaimsLogic and Security-page catalogs exactly as the Work Orders catalog does.
	/// </summary>
	public static class InvoicingPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageInvoicing, PermissionActions.DepartmentAdminsOnly, false, "ManageInvoicingNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewInvoicing, PermissionActions.DepartmentAdminsOnly, false, "ViewInvoicingNote", false)
		};
	}
}
