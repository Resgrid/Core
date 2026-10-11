using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Workforce, pay data and field costing subtree of an authorized department deletion, in the caller's transaction, after
	/// the legal hold checks. Purged whatever its statutory period, as RMS records are (decided 2026-10-11): the department
	/// authorized the deletion and had the grace period to export. Member removal end-dates employment instead; that is the
	/// path that keeps past-period pay data. The tables carry no foreign keys; facts and runs go before the profiles they cite.
	/// </summary>
	public static class WorkforceDepartmentCleanup
	{
		internal static readonly string[] Tables =
		{
			"PayDataExportArtifacts", "PayDataReportRows", "PayDataReportEmployeeSnapshots", "PayDataReportRuns", "PayDataReportingDemographics",
			"WorkforceAnnualPayFacts", "WorkforceWorkEntries", "WorkforceJobAssignments", "EmployeePayComponents", "EmployeeCostComponents",
			"EmployeeCompensationProfiles", "WorkforceEmployments", "WorkforceWorkers", "WorkforceLaborContractors", "WorkforceEstablishments",
			"WorkforceAffiliatedEntities", "WorkforceEmployerProfiles", "FieldCostLines", "FieldCostRuns", "ResourceUsageEntries",
			"ResourceCostComponents", "ResourceCostProfiles"
		};

		public static Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Workforce");
			return DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
