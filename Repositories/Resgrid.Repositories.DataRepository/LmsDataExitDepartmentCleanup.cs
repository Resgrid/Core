using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's Training T5 data exit rows (round 6D, M0395) in the caller's transaction, for both databases: the zips kept in the
	/// database first, then the export jobs. No foreign keys. A zip in the LMS store lives under the department's prefix and expires with its
	/// link (worker 99 removes it).
	/// </summary>
	public static class LmsDataExitDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables = { "LmsDataExportBlobs", "LmsDataExports" };

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS data exit");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
