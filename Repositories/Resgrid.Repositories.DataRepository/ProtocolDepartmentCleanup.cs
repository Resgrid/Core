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
			"ProtocolAiSuggestions", "CallProtocols", "ProtocolAcknowledgments", "ProtocolFileTextPages", "ProtocolFileTexts", "ProtocolVersionFiles", "ProtocolVersionTranslations",
			"ProtocolVersionApprovals", "ProtocolComments", "ProtocolAudiences", "ProtocolVersions", "ProtocolCategories",
			"DispatchProtocolSnapshots", "DispatchProtocolQuestionAnswers", "DispatchProtocolQuestions", "DispatchProtocolAttachments",
			"DispatchProtocolTriggers", "DispatchProtocols"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Protocol");
			string Q(string value) => DepartmentCleanupCommands.Q(type, value);
			var protocols = ("DispatchProtocols", "DispatchProtocolId", "DispatchProtocolId");

			if (!await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "DispatchProtocols", ct))
				return;

			// P4 (M0400): AI suggestions carry the department id and no keys.
			if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "ProtocolAiSuggestions", ct))
				await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "ProtocolAiSuggestions" }, ct);

			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "CallProtocols", false, ct, protocols, ("Calls", "CallId", "CallId"));

			// P2 (M0354) acknowledgments and P1 wave 2 (M0353) file text pages, extractions and primary files reference versions and
			// protocols: they go before the versions.
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolAcknowledgments", true, ct, protocols);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolFileTextPages", true, ct, ("ProtocolFileTexts", "ProtocolFileTextId", "ProtocolFileTextId"));
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolFileTexts", true, ct, protocols);
			// A primary file's key is its version (FK_ProtocolVersionFiles_ProtocolVersions), never its unkeyed protocol id.
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolVersionFiles", true, ct, ("ProtocolVersions", "ProtocolVersionId", "ProtocolVersionId"));
			// P3 (M0380): content translations reference their version.
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolVersionTranslations", true, ct, ("ProtocolVersions", "ProtocolVersionId", "ProtocolVersionId"));

			// P1 (M0343): approval steps, comments and audiences, then the versions they hang off; categories have no keys.
			// Retention and legal hold never stop a department deletion (R-P10).
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolVersionApprovals", true, ct, ("ProtocolVersions", "ProtocolVersionId", "ProtocolVersionId"));
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolComments", true, ct, protocols);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolAudiences", true, ct, protocols);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "ProtocolVersions", true, ct, protocols);
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "ProtocolCategories" }, ct);

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
