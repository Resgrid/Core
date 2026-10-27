using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// The one repository shape behind every Incident Back Office table (plan §11.2): tenant-filtered reads, activation-scoped
	/// paged lists with whitelisted filters, app-generated string keys and compare-and-increment updates. Tables with an invariant
	/// the database enforces (bed-nights, vendor capacity, time immutability) have their own repositories on top of it.
	/// </summary>
	public interface IIncidentLedgerRepository<T> : IRepository<T> where T : class, IIncidentLedgerEntity
	{
		/// <summary>The row, if it belongs to the department (and is live unless <paramref name="includeDeleted"/>).</summary>
		Task<T> GetForDepartmentAsync(int departmentId, string id, bool includeDeleted = false, CancellationToken cancellationToken = default);

		Task<IEnumerable<T>> GetByIdsAsync(int departmentId, IEnumerable<string> ids, CancellationToken cancellationToken = default);

		Task<IEnumerable<T>> ListAsync(int departmentId, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		Task<int> CountAsync(int departmentId, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		/// <summary>Sum of a numeric column over the query's rows (null rows ignored).</summary>
		Task<decimal> SumAsync(int departmentId, string column, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		/// <summary>Allocates the key (lowercase GUID) and the insert stamps, then inserts.</summary>
		Task<T> InsertNewAsync(T entity, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Compare-and-increment: the update applies only while the stored RowVersion equals <paramref name="expectedVersion"/>,
		/// otherwise <see cref="IncidentConcurrencyException"/>. Non-versioned tables update unconditionally.
		/// </summary>
		Task<T> UpdateVersionedAsync(T entity, int expectedVersion, string userId, CancellationToken cancellationToken = default);

		/// <summary>Marks a soft-deletable row deleted (no-op when the table has no IsDeleted).</summary>
		Task<bool> SoftDeleteAsync(int departmentId, string id, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Physically removes the department's rows whose <paramref name="column"/> equals <paramref name="value"/> (optionally only those
		/// on or after <paramref name="fromDate"/> in <paramref name="dateColumn"/>). Only for tables without IsDeleted — derived
		/// rows such as bed-nights, which the owning row's transaction rewrites; refused on soft-deletable tables.
		/// </summary>
		Task<int> PurgeWhereAsync(int departmentId, string column, string value, string dateColumn = null, DateTime? fromDate = null, CancellationToken cancellationToken = default);

		/// <summary>Takes an update lock on the row in the current transaction (UPDLOCK, HOLDLOCK / FOR UPDATE).</summary>
		Task<T> LockForUpdateAsync(int departmentId, string id, CancellationToken cancellationToken = default);
	}

	/// <summary>A whitelisted list filter: column names are checked against the entity's properties before any SQL is built.</summary>
	public sealed class IncidentLedgerQuery
	{
		/// <summary>Rows of this activation (or of several, for a complex roll-up).</summary>
		public IReadOnlyList<string> ActivationIds { get; set; }
		/// <summary>Department catalog rows only (IncidentActivationId IS NULL).</summary>
		public bool DepartmentCatalogOnly { get; set; }
		/// <summary>Department catalog rows plus the rows of <see cref="ActivationIds"/>.</summary>
		public bool IncludeDepartmentCatalog { get; set; }
		public Dictionary<string, object> Equal { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
		public Dictionary<string, IReadOnlyList<int>> InInts { get; } = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
		public Dictionary<string, IReadOnlyList<string>> InStrings { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		public List<string> IsNull { get; } = new List<string>();
		public List<string> IsNotNull { get; } = new List<string>();
		public string RangeColumn { get; set; }
		public DateTime? From { get; set; }
		public DateTime? Before { get; set; }
		/// <summary>Case-insensitive contains over <see cref="SearchColumns"/>.</summary>
		public string Search { get; set; }
		public IReadOnlyList<string> SearchColumns { get; set; }
		public string OrderBy { get; set; }
		public bool Descending { get; set; }
		public string ThenBy { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 500;
		public bool IncludeDeleted { get; set; }

		public static IncidentLedgerQuery ForActivation(string activationId) => new IncidentLedgerQuery { ActivationIds = activationId == null ? null : new[] { activationId } };

		public IncidentLedgerQuery Where(string column, object value) { Equal[column] = value; return this; }
		public IncidentLedgerQuery WhereIn(string column, IReadOnlyList<int> values) { InInts[column] = values; return this; }
		public IncidentLedgerQuery WhereIn(string column, IReadOnlyList<string> values) { InStrings[column] = values; return this; }
		public IncidentLedgerQuery WhereNull(string column) { IsNull.Add(column); return this; }
		public IncidentLedgerQuery WhereNotNull(string column) { IsNotNull.Add(column); return this; }
		public IncidentLedgerQuery Between(string column, DateTime? from, DateTime? before) { RangeColumn = column; From = from; Before = before; return this; }
		public IncidentLedgerQuery Order(string column, bool descending = false, string thenBy = null) { OrderBy = column; Descending = descending; ThenBy = thenBy; return this; }
		public IncidentLedgerQuery Page(int skip, int take) { Skip = skip; Take = take; return this; }
	}
}
