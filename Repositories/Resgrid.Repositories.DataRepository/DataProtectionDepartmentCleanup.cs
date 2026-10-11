using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Advanced Data Protection (ADP) state of an authorized department deletion, in the caller's transaction (decided
	/// 2026-10-11). The department's wrapped data keys are deleted last, after every table that holds its ciphertext, so the
	/// delete is also a crypto-shred of any copy the purge cannot reach (caches, queue payloads). The Transit KEK is shared and
	/// derived, so there is no per-department key to destroy in OpenBao, and database backups keep their own copy until they
	/// age out. AdpAuditEvents is the value-free, hash-chained ADP audit and is retained by design.
	/// </summary>
	public static class DataProtectionDepartmentCleanup
	{
		public const string MigrationLockMessage = "Department deletion is waiting for a running protected data migration to release its department lock.";

		/// <summary>The tables this cleanup deletes, keys last.</summary>
		internal static readonly string[] Tables =
		{
			"DepartmentProtectedDataEgressPolicies", "DepartmentDataProtectionMigrations", "DepartmentDataProtectionPolicies", "DepartmentOperationLocks",
			"DepartmentDataProtectionKeys"
		};

		/// <summary>
		/// Defers the deletion while the ADP migration worker holds a live department lock (unreleased and unexpired; the
		/// coordinator releases expired ones). On SQL Server the range lock also keeps a new lock from being acquired until the
		/// deletion commits. Call before any delete, so a deferred attempt does no work.
		/// </summary>
		public static async Task ThrowIfOperationInProgressAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			if (await HasLiveOperationLockAsync(connection, transaction, departmentId, type, ct)) throw new DepartmentDeletionDeferredException(MigrationLockMessage);
		}

		/// <summary>True while the ADP migration worker holds a live lock on the department; see <see cref="ThrowIfOperationInProgressAsync"/>.</summary>
		internal static async Task<bool> HasLiveOperationLockAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Data protection");
			if (!await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "DepartmentOperationLocks", ct)) return false;
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(type == DatabaseTypes.Postgres
				? "SELECT COUNT(*) FROM (SELECT 1 FROM departmentoperationlocks WHERE departmentid=@DepartmentId AND releasedutc IS NULL AND expiresutc > (now() AT TIME ZONE 'utc') FOR UPDATE) l"
				: "SELECT COUNT(*) FROM [DepartmentOperationLocks] WITH (UPDLOCK,HOLDLOCK) WHERE [DepartmentId]=@DepartmentId AND [ReleasedUtc] IS NULL AND [ExpiresUtc] > SYSUTCDATETIME()",
				new { DepartmentId = departmentId }, transaction, cancellationToken: ct)) > 0;
		}

		/// <summary>Deletes the department's ADP policy, migration, egress and lock rows, then its wrapped keys. Run after every
		/// other department delete in the transaction.</summary>
		public static Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Data protection");
			return DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
