using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Called only from an authorized department-deletion transaction, after the shared hold fence.</summary>
	public static class AdminAssistDepartmentCleanup
	{
		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			if (departmentId <= 0 || transaction?.Connection != connection) throw new InvalidOperationException("A department-deletion transaction is required.");
			var pg = type == DatabaseTypes.Postgres;
			string Q(string value) => pg ? value.ToLowerInvariant() : "[" + value + "]";
			foreach (var table in new[] { "AdminAssistDispatchTraces", "AdminAssistFindings", "AdminAssistDailySummaries", "AdminAssistPreferences", "AdminAssistLearning", "AdminAssistHistory", "AdminAssistWorkerStates", "AdminAssistWorkspaces", "AdminAssistConfigurationRevisions" })
			{
				var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT CASE WHEN to_regclass(@Name) IS NULL THEN 0 ELSE 1 END" : "SELECT CASE WHEN OBJECT_ID(@Name,'U') IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 0 ELSE -1 END", new { Name = (pg ? "public." + table.ToLowerInvariant() : "dbo." + table) }, transaction, cancellationToken: ct));
				if (exists < 0) throw new InvalidOperationException("Cannot verify the Admin Assist cleanup schema.");
				if (exists == 1) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=@DepartmentId", new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			}
		}
	}
}
