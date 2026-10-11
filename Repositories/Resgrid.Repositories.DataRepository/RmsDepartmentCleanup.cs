using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Records (RMS) subtree of a department deletion, in the caller's transaction. Department deletion purges every RMS
	/// row of the department whatever its retention period: the department authorized the deletion and had the grace
	/// period to export. Only an active legal hold blocks it (RMS plan, legal hold). Tables are discovered from the
	/// schema, every Rms* table with a DepartmentId column, so a new RMS table cannot be left behind the way FK-less
	/// tables are by the hand-written delete script. RMS tables carry no foreign keys, so delete order does not matter.
	/// RmsSearchIndexStates is kept: it holds no record content and is the marker the records search sweep uses to
	/// erase the department's Lucene documents before removing it.
	/// </summary>
	public static class RmsDepartmentCleanup
	{
		public const string SearchErasureMarkerTable = "RmsSearchIndexStates";
		public const string LegalHoldMessage = "Department deletion is waiting for an active Records legal hold to be released.";

		/// <summary>
		/// Deletes the department's RMS rows. A live department (<paramref name="orphaned"/> false) must exist and is locked;
		/// an active legal hold throws <see cref="DepartmentDeletionDeferredException"/>. An orphaned department, one whose
		/// Departments row is already gone, must not exist; an active legal hold leaves it untouched and returns false.
		/// </summary>
		public static async Task<bool> DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, bool orphaned, CancellationToken ct = default)
		{
			if (transaction == null || transaction.Connection != connection || departmentId <= 0) throw new InvalidOperationException("RMS department cleanup requires a scoped transaction.");
			var pg = type == DatabaseTypes.Postgres;
			string Q(string value) => pg ? value.ToLowerInvariant() : "[" + value + "]";
			var tables = await DepartmentTablesAsync(connection, transaction, type, ct);
			if (tables.Count == 0) return true;

			var parameters = new { DepartmentId = departmentId, Producer = DomainEventProducers.Records };
			var department = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(pg
				? "SELECT departmentid FROM departments WHERE departmentid=@DepartmentId FOR UPDATE"
				: "SELECT DepartmentId FROM Departments WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId", parameters, transaction, cancellationToken: ct));
			if (orphaned && department.HasValue) throw new InvalidOperationException("An orphaned RMS cleanup cannot purge a department that still exists.");
			if (!orphaned && department != departmentId) throw new InvalidOperationException("RMS department cleanup could not lock the requested department.");

			if (tables.Contains("RmsRecordLegalHolds") && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ReleasedOn")} IS NULL", parameters, transaction, cancellationToken: ct)) > 0)
			{
				if (orphaned) return false;
				throw new DepartmentDeletionDeferredException(LegalHoldMessage);
			}

			// Records-owned rows in shared tables carry record content: event payloads and rendered workflow output.
			if (await HasColumnAsync(connection, transaction, type, "DomainEventOutbox", "ProducerSubsystem", ct))
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("DomainEventOutbox")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ProducerSubsystem")}=@Producer", parameters, transaction, cancellationToken: ct));
			var recordIds = new[] { ("RmsOperationalRecords", "RmsOperationalRecordId"), ("RmsIncidentReports", "RmsIncidentReportId"), ("RmsIncidentAnalyses", "RmsIncidentAnalysisId") }
				.Where(r => tables.Contains(r.Item1))
				.Select(r => $"SELECT {Q(r.Item2)} FROM {Q(r.Item1)} WHERE {Q("DepartmentId")}=@DepartmentId").ToList();
			if (recordIds.Count > 0 && await HasColumnAsync(connection, transaction, type, "WorkflowRuns", "AggregateId", ct))
			{
				var runs = $"SELECT {Q("WorkflowRunId")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("AggregateId")} IN ({string.Join(" UNION ALL ", recordIds)})";
				if (await HasColumnAsync(connection, transaction, type, "WorkflowRunLogs", "WorkflowRunId", ct))
					await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")} IN ({runs})", parameters, transaction, cancellationToken: ct));
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRuns")} WHERE {Q("WorkflowRunId")} IN ({runs})", parameters, transaction, cancellationToken: ct));
			}

			foreach (var table in tables.Where(t => !string.Equals(t, SearchErasureMarkerTable, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
			return true;
		}

		/// <summary>Departments that still own RMS rows (other than the search erasure marker) but no longer exist.</summary>
		public static async Task<List<int>> GetOrphanedDepartmentIdsAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, CancellationToken ct = default)
		{
			var pg = type == DatabaseTypes.Postgres;
			string Q(string value) => pg ? value.ToLowerInvariant() : "[" + value + "]";
			var tables = (await DepartmentTablesAsync(connection, transaction, type, ct)).Where(t => !string.Equals(t, SearchErasureMarkerTable, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
			if (tables.Count == 0) return new List<int>();
			var owners = string.Join(" UNION ", tables.Select(t => $"SELECT {Q("DepartmentId")} AS {Q("DepartmentId")} FROM {Q(t)} WHERE {Q("DepartmentId")} IS NOT NULL"));
			return (await connection.QueryAsync<int>(new CommandDefinition(
				$"SELECT o.{Q("DepartmentId")} FROM ({owners}) o WHERE NOT EXISTS (SELECT 1 FROM {Q("Departments")} d WHERE d.{Q("DepartmentId")}=o.{Q("DepartmentId")}) ORDER BY o.{Q("DepartmentId")}",
				transaction: transaction, cancellationToken: ct))).ToList();
		}

		/// <summary>Every Rms* table with a DepartmentId column. Global catalogs without one are not department data.</summary>
		public static async Task<HashSet<string>> DepartmentTablesAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, CancellationToken ct = default)
		{
			var pg = type == DatabaseTypes.Postgres;
			var tables = new HashSet<string>(await connection.QueryAsync<string>(new CommandDefinition(pg
				? "SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='departmentid' AND table_name LIKE 'rms%'"
				: "SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name LIKE 'Rms%' AND c.name='DepartmentId'",
				transaction: transaction, cancellationToken: ct)), StringComparer.OrdinalIgnoreCase);
			if (tables.Count > 0) return tables;
			// An empty result must mean RMS is not installed, not that this principal cannot see the schema.
			var cutovers = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg
				? "SELECT CASE WHEN to_regclass('public.rmsdepartmentcutovers') IS NULL THEN 0 ELSE 1 END"
				: "SELECT CASE WHEN OBJECT_ID('dbo.RmsDepartmentCutovers','U') IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 0 ELSE -1 END",
				transaction: transaction, cancellationToken: ct));
			if (cutovers != 0) throw new InvalidOperationException("Cannot verify the RMS schema for department cleanup.");
			return tables;
		}

		private static async Task<bool> HasColumnAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, string table, string column, CancellationToken ct)
		{
			var pg = type == DatabaseTypes.Postgres;
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg
				? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name=@Table AND column_name=@Column"
				: "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(@Table,'U') AND name=@Column",
				new { Table = pg ? table.ToLowerInvariant() : "dbo." + table, Column = pg ? column.ToLowerInvariant() : column }, transaction, cancellationToken: ct)) > 0;
		}
	}
}
