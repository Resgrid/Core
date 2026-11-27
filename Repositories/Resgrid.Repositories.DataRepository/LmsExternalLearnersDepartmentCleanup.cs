using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T5 external-learner rows (round 6E, M0396-M0397) in the caller's transaction, for both databases: class fees,
	/// public classes, course access, learner tokens, learners and the portal. No foreign keys (LMS tables reference baseline ids only, §17.3).
	/// The learners' LMS rows (enrolments, progress, rosters, learning records) carry the department id and go with their own cleanups.
	/// </summary>
	public static class LmsExternalLearnersDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsClassFees", "LmsPublicClasses", "LmsExternalCourseAccess", "LmsLearnerNotices", "LmsExternalLearnerTokens", "LmsExternalLearners", "LmsLearnerPortals"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS external learners");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
