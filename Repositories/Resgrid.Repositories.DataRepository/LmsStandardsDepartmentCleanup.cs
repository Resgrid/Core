using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T5 standards rows (round 6C, M0392-M0394) in the caller's transaction, children first, for both databases: the
	/// cmi5 statements and state documents and the launch sessions before their registrations, AGS tokens and platform keys before the tools,
	/// the xAPI outbox and the content packages. No foreign keys (LMS tables reference baseline ids only, §17.3). The package files in the LMS
	/// media store are not removed here (like hosted video; owed to a store purge).
	/// </summary>
	public static class LmsStandardsDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsCmi5StateDocuments", "LmsCmi5Statements", "LmsRuntimeSessions", "LmsRuntimeRegistrations", "LmsLtiAccessTokens", "LmsLtiPlatformKeys", "LmsLtiTools",
			"LmsXapiOutbox", "LmsContentPackages"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS standards");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
