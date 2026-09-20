using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable deployment and contractor permissions (Workforce &amp; Business Operations plan, C8;
	/// registry 116-119). All fall back to department administrators. Rostered members always see their own
	/// deployments and file time entries without any of them, and file their own incident-bound F-42 / expense drafts
	/// without ManageMutualAidReimbursement (79).
	/// </summary>
	public static class DeploymentPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageDeployments, PermissionActions.DepartmentAdminsOnly, false, "ManageDeploymentsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ApproveTimeReports, PermissionActions.DepartmentAdminsOnly, false, "ApproveTimeReportsNote", false),
			// Contractor path (C-M2, 2026-09-19): bids and service contracts / compliance documents.
			new RecordPermissionDescriptor(PermissionTypes.ManageBids, PermissionActions.DepartmentAdminsOnly, false, "ManageBidsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageContracts, PermissionActions.DepartmentAdminsOnly, false, "ManageContractsNote", false),
			// Cal OES MARS (C-M3, 2026-09-19): agency / rate / agreement management, portal handoff, external observation and invoice reconciliation.
			new RecordPermissionDescriptor(PermissionTypes.ManageMutualAidReimbursement, PermissionActions.DepartmentAdminsOnly, false, "ManageMutualAidReimbursementNote", false)
		};
	}
}
