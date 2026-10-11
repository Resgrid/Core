using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Statements shared by the module cleanups of an authorized department deletion. Everything runs in the
	/// caller's transaction; nothing here commits or grants independent purge access.</summary>
	internal static class DepartmentCleanupCommands
	{
		internal static void RequireTransaction(DbConnection connection, DbTransaction transaction, int departmentId, string module)
		{
			if (transaction == null || transaction.Connection != connection || departmentId <= 0)
				throw new InvalidOperationException(module + " department cleanup requires the caller's scoped transaction.");
		}

		internal static string Q(DatabaseTypes type, string value) => type == DatabaseTypes.Postgres ? value.ToLowerInvariant() : "[" + value + "]";

		/// <summary>True when the table exists. A principal that cannot see the schema throws instead of reading as "no table".</summary>
		internal static async Task<bool> ExistsAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, string table, CancellationToken ct)
		{
			var pg = type == DatabaseTypes.Postgres;
			var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg
				? "SELECT CASE WHEN to_regclass(@Name) IS NULL THEN 0 ELSE 1 END"
				: "SELECT CASE WHEN OBJECT_ID(@Name,'U') IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 0 ELSE -1 END",
				new { Name = pg ? "public." + table.ToLowerInvariant() : "dbo." + table }, transaction, cancellationToken: ct));
			if (result < 0) throw new InvalidOperationException("Cannot verify the department cleanup schema.");
			return result == 1;
		}

		/// <summary>Deletes the department's rows from each table in the order given. Tables this schema lacks are skipped.</summary>
		internal static async Task DeleteByDepartmentAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, int departmentId, IEnumerable<string> tables, CancellationToken ct)
		{
			foreach (var table in tables)
				if (await ExistsAsync(connection, transaction, type, table, ct))
					await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(type, table)} WHERE {Q(type, "DepartmentId")}=@DepartmentId", new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
		}

		/// <summary>Deletes the rows of a foreign-keyed child that belong to the department or reference one of its parent rows
		/// (<paramref name="parents"/>: parent table and the child column that references its key).</summary>
		internal static async Task DeleteChildAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, int departmentId, string child, bool childHasDepartment, CancellationToken ct, params (string Parent, string Key, string ChildColumn)[] parents)
		{
			if (!await ExistsAsync(connection, transaction, type, child, ct)) return;
			var predicates = new List<string>();
			if (childHasDepartment) predicates.Add($"{Q(type, "DepartmentId")}=@DepartmentId");
			foreach (var (parent, key, childColumn) in parents)
				if (await ExistsAsync(connection, transaction, type, parent, ct))
					predicates.Add($"{Q(type, childColumn)} IN (SELECT {Q(type, key)} FROM {Q(type, parent)} WHERE {Q(type, "DepartmentId")}=@DepartmentId)");
			if (predicates.Count == 0) return;
			await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(type, child)} WHERE {string.Join(" OR ", predicates)}", new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
		}
	}
}
