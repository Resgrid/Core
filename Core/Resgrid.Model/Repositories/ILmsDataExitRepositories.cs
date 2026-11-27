using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T5 data exit (round 6D, M0395). Every read is department scoped; the export rows never carry the zip's bytes.

	public interface ILmsDataExportsRepository : IRepository<LmsDataExport>
	{
		/// <summary>The department's exports, newest first (at most <paramref name="take"/>).</summary>
		Task<List<LmsDataExport>> GetForDepartmentAsync(int departmentId, int take = 50);

		Task<LmsDataExport> GetByIdForDepartmentAsync(int departmentId, int lmsDataExportId);

		/// <summary>Queued exports, oldest first (worker 99's due-work query).</summary>
		Task<List<LmsDataExport>> GetQueuedAsync(int take);

		/// <summary>Atomically moves a Queued export to Running and counts the attempt; false when another worker took it.</summary>
		Task<bool> TryClaimAsync(int lmsDataExportId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Running exports claimed before <paramref name="claimedBeforeUtc"/> go back to Queued (a stopped worker). Returns how many.</summary>
		Task<int> RequeueStaleAsync(DateTime claimedBeforeUtc, CancellationToken cancellationToken = default);

		/// <summary>Completed exports past their expiry whose zip is not removed yet (worker 99's purge query).</summary>
		Task<List<LmsDataExport>> GetExpiredUnpurgedAsync(DateTime utcNow, int take);
	}

	public interface ILmsDataExportBlobsRepository : IRepository<LmsDataExportBlob>
	{
		/// <summary>The zip of an export kept in the database; null when there is none.</summary>
		Task<byte[]> GetDataAsync(int departmentId, int lmsDataExportId);

		Task<int> DeleteForExportAsync(int departmentId, int lmsDataExportId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The data exit's reader: every row of one department from an allow-listed table (the tables the export writes; none of them holds
	/// file bytes), in id order. Department scoped by construction: the only filter is the department id.
	/// </summary>
	public interface ILmsDataExportSourceRepository
	{
		/// <summary>Every row of <typeparamref name="TRow"/>'s table for the department. Throws for a table that is not allow-listed.</summary>
		Task<List<TRow>> GetDepartmentRowsAsync<TRow>(int departmentId, CancellationToken cancellationToken = default) where TRow : class, IEntity, new();
	}
}
