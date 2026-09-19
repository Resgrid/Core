using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-configurable deployment-core permissions (Workforce &amp; Business Operations plan, C8; registry 118-119).
	/// Both fall back to department administrators. Rostered members always see their own deployments and file time
	/// entries without either; ManageBids (116), ManageContracts (117) and ManageMutualAidReimbursement (79) join this
	/// list with the contractor-billing and Cal OES MARS milestones.
	/// </summary>
	public static class DeploymentPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ManageDeployments, PermissionActions.DepartmentAdminsOnly, false, "ManageDeploymentsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ApproveTimeReports, PermissionActions.DepartmentAdminsOnly, false, "ApproveTimeReportsNote", false)
		};
	}
}
