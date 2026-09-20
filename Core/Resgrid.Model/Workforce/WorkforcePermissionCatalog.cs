using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable workforce permissions (Workforce &amp; Business Operations plan, Phase E; registry
	/// 74-78). All fall back to department administrators. A member always sees and answers their own demographic
	/// response and files their own resource usage without any of them; every protected value additionally needs a
	/// current Protected Data Grant to render.
	/// </summary>
	public static class WorkforcePermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ViewInternalCosts, PermissionActions.DepartmentAdminsOnly, false, "ViewInternalCostsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageWorkforceCompensation, PermissionActions.DepartmentAdminsOnly, false, "ManageWorkforceCompensationNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewWorkforceCompensation, PermissionActions.DepartmentAdminsOnly, false, "ViewWorkforceCompensationNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManagePayDataReporting, PermissionActions.DepartmentAdminsOnly, false, "ManagePayDataReportingNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ExportPayDataReporting, PermissionActions.DepartmentAdminsOnly, false, "ExportPayDataReportingNote", false)
		};
	}
}
