using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's protocols and everything hanging off them, in the caller's transaction, children first, written for both
	/// databases (D23: the cleanup was in the SQL Server script only). CallProtocols no longer cascades from DispatchProtocols
	/// (M0333), so every call row citing one of the department's protocols goes first, with the department's own calls' rows;
	/// then the snapshots of the text as dispatched (M0337), answers, questions, attachments, triggers and the protocols.
	/// </summary>
	public static class ProtocolDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"CallProtocols", "DispatchProtocolSnapshots", "DispatchProtocolQuestionAnswers", "DispatchProtocolQuestions",
			"DispatchProtocolAttachments", "DispatchProtocolTriggers", "DispatchProtocols"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Protocol");
			string Q(string value) => DepartmentCleanupCommands.Q(type, value);
			var protocols = ("DispatchProtocols", "DispatchProtocolId", "DispatchProtocolId");

			if (!await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "DispatchProtocols", ct))
				return;

			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "CallProtocols", false, ct, protocols, ("Calls", "CallId", "CallId"));
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "DispatchProtocolSnapshots", true, ct, protocols);

			if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "DispatchProtocolQuestionAnswers", ct))
			{
				await connection.ExecuteAsync(new CommandDefinition(
					$"DELETE FROM {Q("DispatchProtocolQuestionAnswers")} WHERE {Q("DispatchProtocolQuestionId")} IN " +
					$"(SELECT q.{Q("DispatchProtocolQuestionId")} FROM {Q("DispatchProtocolQuestions")} q WHERE q.{Q("DispatchProtocolId")} IN " +
					$"(SELECT p.{Q("DispatchProtocolId")} FROM {Q("DispatchProtocols")} p WHERE p.{Q("DepartmentId")}=@DepartmentId))",
					new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			}

			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "DispatchProtocolQuestions", false, ct, protocols);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "DispatchProtocolAttachments", false, ct, protocols);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "DispatchProtocolTriggers", false, ct, protocols);
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "DispatchProtocols" }, ct);
		}
	}
}
