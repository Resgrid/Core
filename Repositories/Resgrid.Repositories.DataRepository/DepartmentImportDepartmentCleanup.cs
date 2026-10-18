using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Department Import (Import &amp; Migration) subtree of an authorized department deletion, in the caller's transaction
	/// (wizard plan section 8.6, conformance I5). Every table carries DepartmentId and no foreign key; children go before the
	/// workspaces they hang off. The artifact bytes live in the RustFS object store, not here: the import maintenance worker
	/// (48) removes a department's objects once its workspace rows are gone.
	/// </summary>
	public static class DepartmentImportDepartmentCleanup
	{
		internal static readonly string[] Tables =
		{
			"ImportConfigurationChanges", "ImportEvents", "ImportSuggestions", "ImportMessages", "ImportSupportRequests", "ImportSupportGrants", "ImportReconciliations",
			"ImportChangeJournal", "ImportRuns", "ImportExternalIds", "ImportIssues", "ImportStagedRows", "ImportValueMaps",
			"ImportColumnMaps", "ImportDatasets", "ImportArtifacts", "ImportSources", "ImportWorkspaces"
		};

		public static Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "DepartmentImport");
			return DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
