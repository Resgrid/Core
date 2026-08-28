using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Bulk data access for the ADP migration engine. Table and column identifiers come exclusively
	/// from code-reviewed AdpTableBinding constants (never runtime input); values are always bound as
	/// parameters. Batch writes and the migration row's cursor advance commit in ONE transaction, so
	/// a crash re-processes at most one batch — which the engine's double-encryption guard makes a
	/// no-op (plan section 19.4). No method stages plaintext in temp tables or files.
	/// </summary>
	public interface IDepartmentDataProtectionBulkRepository
	{
		/// <summary>Total department-owned rows in the bound table (sizing and progress).</summary>
		Task<long> CountRowsAsync(AdpTableBinding binding, int departmentId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The next batch of department-owned rows strictly after the cursor, ordered by primary key.
		/// Selects the bound columns plus companion/marker columns.
		/// </summary>
		Task<IReadOnlyList<AdpBulkFieldRow>> GetBatchAsync(AdpTableBinding binding, int departmentId,
			string afterCursor, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>
		/// Applies the row updates and advances the migration row's cursor and counters in one
		/// transaction. Passing no updates still advances the cursor (a batch of already-protected
		/// rows moves forward durably).
		/// </summary>
		Task ApplyBatchAsync(AdpTableBinding binding, IReadOnlyList<AdpBulkRowUpdate> updates,
			int departmentDataProtectionMigrationId, string newCursor, long rowsProcessedDelta,
			long rowsAlreadyProtectedDelta, long rowsAnomalousDelta, CancellationToken cancellationToken);

		/// <summary>
		/// Residue scan for text columns. enveloped=false counts rows still holding plaintext in any
		/// bound text column (enrollment verification must find zero); enveloped=true counts rows
		/// still holding an rgdp: envelope (offboarding verification must find zero).
		/// </summary>
		Task<long> CountTextResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default);

		/// <summary>Residue scan for binary columns (rgdpb header prefix compare).</summary>
		Task<long> CountBinaryResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Residue scan for companion-column fields: enrollment residue = typed column still non-null;
		/// offboarding residue = companion envelope column still non-null.
		/// </summary>
		Task<long> CountCompanionResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped,
			CancellationToken cancellationToken = default);
	}
}
