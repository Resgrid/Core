using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Low-level maintenance access used by the nightly UTF-8 data cleanup worker. It enumerates
	/// every free-form text column in the database, reads rows in keyset-paginated batches, and
	/// writes back repaired values so the data stays safe for a PostgreSQL UTF-8 migration. Works
	/// against whichever backend is currently configured (SQL Server source or PostgreSQL target).
	/// </summary>
	public interface IUtf8MaintenanceRepository
	{
		/// <summary>
		/// Returns every base-table text column grouped by table, restricted to tables that have a
		/// single-column primary key (required for stable keyset pagination).
		/// </summary>
		Task<List<Utf8TextColumnTarget>> GetTextColumnTargetsAsync(CancellationToken cancellationToken);

		/// <summary>
		/// Reads the next batch of rows for <paramref name="target"/> ordered by primary key, starting
		/// after <paramref name="lastKey"/> (null/empty for the first page).
		/// </summary>
		Task<Utf8RowBatch> GetRowBatchAsync(Utf8TextColumnTarget target, string lastKey, int batchSize, CancellationToken cancellationToken);

		/// <summary>
		/// Applies the supplied repaired rows. Each row carries only the columns that changed.
		/// Returns the number of rows updated.
		/// </summary>
		Task<int> UpdateRowsAsync(Utf8TextColumnTarget target, IReadOnlyList<Utf8TextRow> rows, CancellationToken cancellationToken);

		/// <summary>Loads the saved cleanup watermark for a table, or null if none exists yet.</summary>
		Task<Utf8CleanupProgress> GetProgressAsync(string tableKey, CancellationToken cancellationToken);

		/// <summary>Inserts or updates the cleanup watermark for a table.</summary>
		Task SaveProgressAsync(Utf8CleanupProgress progress, CancellationToken cancellationToken);
	}

	/// <summary>A table plus its single-column primary key and the free-form text columns to scan.</summary>
	public class Utf8TextColumnTarget
	{
		public string Schema { get; set; }
		public string TableName { get; set; }
		public string PrimaryKeyColumn { get; set; }
		public List<string> TextColumns { get; set; } = new List<string>();

		/// <summary>Stable identifier used as the watermark key (schema-qualified table name).</summary>
		public string Key => string.IsNullOrEmpty(Schema) ? TableName : Schema + "." + TableName;
	}

	/// <summary>A page of rows plus the primary key of the last row (the next page's start cursor).</summary>
	public class Utf8RowBatch
	{
		public List<Utf8TextRow> Rows { get; set; } = new List<Utf8TextRow>();
		public string LastKey { get; set; }
	}

	/// <summary>
	/// A single row's primary key and text-column values. When returned from a read it holds all
	/// scanned columns; when passed to <see cref="IUtf8MaintenanceRepository.UpdateRowsAsync"/> it
	/// holds only the columns that changed.
	/// </summary>
	public class Utf8TextRow
	{
		public string Key { get; set; }
		public Dictionary<string, string> Columns { get; set; } = new Dictionary<string, string>();
	}

	/// <summary>Per-table cleanup watermark, persisted so each nightly run resumes where it stopped.</summary>
	public class Utf8CleanupProgress
	{
		public string TableName { get; set; }
		public string LastProcessedKey { get; set; }
		public DateTime? LastCompletedUtc { get; set; }
		public long RowsScanned { get; set; }
		public long RowsFixed { get; set; }
		public DateTime UpdatedOnUtc { get; set; }
	}
}
