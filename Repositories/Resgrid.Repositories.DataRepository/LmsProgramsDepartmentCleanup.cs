using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T3 part 2 rows (round 4C, M0367-M0370) in the caller's transaction, children first, for both databases:
	/// annual plans, competency frameworks and tags, training requests and decisions, saved reports, folders and runs, certification cycle
	/// rules and alert claims, and worker 94's state. No foreign keys (LMS tables reference baseline ids only, §17.3).
	/// </summary>
	public static class LmsProgramsDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsTrainingPlanItems", "LmsTrainingPlans", "LmsCompetencyAlignments", "LmsCompetencyItems", "LmsCompetencyFrameworks",
			"LmsTrainingRequestDecisions", "LmsTrainingRequests", "LmsReportRuns", "LmsReportDefinitions", "LmsReportFolders",
			"LmsCertificationAlerts", "LmsCertificationCycleRules", "LmsProgramStates",
			// Round 9A (M0405-M0406): certification requirement sets, their items and the issuance ledger.
			"LmsCertificationRequirementItems", "LmsCertificationRequirementSets", "LmsCertificationIssuances"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS programs");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
