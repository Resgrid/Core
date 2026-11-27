using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's protocol and course set rows and LMS sponsorships (round 10 A3, M0415-M0416) in the caller's transaction, for both
	/// databases. The department is removed as either party: its own rows by DepartmentId (its sets as publisher; its subscriptions, copies,
	/// inbox and proposals as subscriber; sponsorships it receives), then the other departments' rows that point at it as publisher or sponsor
	/// (their subscriptions to its sets, the copy bookkeeping, inbox rows and proposals, the sponsorships it paid for). The subscribers' local
	/// protocols, courses and trainings are their own content and stay. No foreign keys.
	/// </summary>
	public static class ContentSetsDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"ContentSetUpdates", "ContentSetProposals", "ContentSetCopies", "ContentSetSubscriptions", "ContentSetItems", "ContentSets", "LmsSponsorships"
		};

		/// <summary>The other party's column on each table that has one (rows other departments hold about this department).</summary>
		internal static readonly (string Table, string Column)[] CrossPartyColumns =
		{
			("ContentSetUpdates", "PublisherDepartmentId"),
			("ContentSetProposals", "PublisherDepartmentId"),
			("ContentSetCopies", "PublisherDepartmentId"),
			("ContentSetSubscriptions", "PublisherDepartmentId"),
			("LmsSponsorships", "SponsorDepartmentId")
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Content sets");

			foreach (var (table, column) in CrossPartyColumns)
				if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, table, ct))
					await connection.ExecuteAsync(new CommandDefinition(
						$"DELETE FROM {DepartmentCleanupCommands.Q(type, table)} WHERE {DepartmentCleanupCommands.Q(type, column)}=@DepartmentId",
						new { DepartmentId = departmentId }, transaction, cancellationToken: ct));

			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
