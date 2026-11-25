using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T4 part 2 rows (round 5C, M0407-M0410) in the caller's transaction, children first, for both databases:
	/// evaluation evidence and members before their evaluations, task books before their definitions, sheet versions before their sheets,
	/// and the skill notification queue. No foreign keys (LMS tables reference baseline ids only, §17.3).
	/// </summary>
	public static class LmsSkillsDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsSkillEvaluationFiles", "LmsSkillEvaluationMembers", "LmsSkillEvaluations", "LmsTaskBooks", "LmsTaskBookDefinitions",
			"LmsSkillSheetVersions", "LmsSkillSheets", "LmsSkillNotifications"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS skills");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
