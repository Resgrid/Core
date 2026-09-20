using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable deployment and contractor permissions (Workforce &amp; Business Operations plan, C8;
	/// registry 116-119). All fall back to department administrators. Rostered members always see their own
	/// deployments and file time entries without any of them; ManageMutualAidReimbursement (79) joins this list with
	/// the Cal OES MARS milestone.
	/// </summary>
	public static class DeploymentPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageDeployments, PermissionActions.DepartmentAdminsOnly, false, "ManageDeploymentsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ApproveTimeReports, PermissionActions.DepartmentAdminsOnly, false, "ApproveTimeReportsNote", false),
			// Contractor path (C-M2, 2026-09-19): bids and service contracts / compliance documents.
			new RecordPermissionDescriptor(PermissionTypes.ManageBids, PermissionActions.DepartmentAdminsOnly, false, "ManageBidsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageContracts, PermissionActions.DepartmentAdminsOnly, false, "ManageContractsNote", false)
		};
	}
}
