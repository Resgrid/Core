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
		/// single-column primary key of a pageable type — text/citext, an integer type, or uuid
		/// (required for stable keyset pagination). Tables whose PK is some other type are skipped.
		/// </summary>
		Task<List<Utf8TextColumnTarget>> GetTextColumnTargetsAsync(CancellationToken cancellationToken);

		/// <summary>
		/// Reads the next batch of rows for <paramref name="target"/> ordered by primary key, starting
		/// after <paramref name="lastKey"/> (null/empty for the first page). The cursor is the previous
		/// page's last key rendered as an invariant string; the implementation re-binds it to the PK's
		/// native type (see <see cref="Utf8TextColumnTarget.PrimaryKeyType"/>) so the keyset comparison
		/// works for text, integer and uuid keys alike.
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

	/// <summary>
	/// The pageable category of a table's primary key. Keyset pagination renders the cursor as an
	/// invariant string and re-binds it as this type for the <c>&gt;</c> / <c>=</c> predicates, so only
	/// these PK types are swept; tables with any other PK type are skipped.
	/// </summary>
	public enum Utf8PrimaryKeyType
	{
		/// <summary>char/varchar/nchar/nvarchar/text/ntext and PostgreSQL citext — bound as a string.</summary>
		Text = 0,

		/// <summary>tinyint/smallint/int/bigint (and PostgreSQL integer types) — bound as Int64.</summary>
		Integer = 1,

		/// <summary>SQL Server uniqueidentifier / PostgreSQL uuid — bound as a Guid.</summary>
		Guid = 2
	}

	/// <summary>A table plus its single-column primary key and the free-form text columns to scan.</summary>
	public class Utf8TextColumnTarget
	{
		public string Schema { get; set; }
		public string TableName { get; set; }
		public string PrimaryKeyColumn { get; set; }

		/// <summary>The PK's value category, so keyset cursors are bound as the PK's native type.</summary>
		public Utf8PrimaryKeyType PrimaryKeyType { get; set; }

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
		/// <summary>The row's primary key rendered as an invariant string (re-bound to its native type for queries).</summary>
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
