using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T4 part 1 rows (round 5B, M0378-M0380) in the caller's transaction, children first, for both databases:
	/// resource bookings, cost lines, instructors and rosters before their class sessions, training resources, and lesson plan versions
	/// before their plans. No foreign keys (LMS tables reference baseline ids only, §17.3). Department deletion purges every roster
	/// whatever its retention (the retention hold stops the roster purge, not the department's deletion).
	/// </summary>
	public static class LmsClassesDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsClassResourceBookings", "LmsClassCostLines", "LmsClassInstructors", "LmsClassEnrollments", "LmsClassSessions", "LmsTrainingResources",
			"LmsLessonPlanVersions", "LmsLessonPlans"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS classes");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
