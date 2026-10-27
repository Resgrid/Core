using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.IncidentLogistics
{
	/// <summary>
	/// The shared Incident Back Office repository (plan §11.2), registered as an open generic for every table. Every query starts
	/// at DepartmentId; filter, order and search columns are checked against the entity's mapped properties before any SQL is
	/// built, so a column name never reaches the database unchecked. Lists use = ANY(@p::citext[]) on PostgreSQL (the citext
	/// key index) and IN @p on SQL Server.
	/// </summary>
	public class IncidentLedgerRepository<T> : RmsRepositoryBase<T>, IIncidentLedgerRepository<T> where T : class, IIncidentLedgerEntity, new()
	{
		private static readonly T Shape = new T();
		private static readonly HashSet<string> Mapped = new HashSet<string>(
			typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<NotMappedAttribute>() == null && !Shape.IgnoredProperties.Contains(p.Name))
				.Select(p => p.Name), StringComparer.Ordinal);
		private static readonly Dictionary<string, Type> Types = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => Mapped.Contains(p.Name)).ToDictionary(p => p.Name, p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType, StringComparer.Ordinal);

		public IncidentLedgerRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected static string Table => Shape.TableName;
		protected static string Key => Shape.IdName;
		protected static bool SoftDeletable => typeof(IIncidentSoftDeletable).IsAssignableFrom(typeof(T));
		protected static bool Versioned => typeof(IIncidentVersioned).IsAssignableFrom(typeof(T));
		protected static bool HasActivation => Mapped.Contains("IncidentActivationId");
		protected static string False => IsPostgres ? "FALSE" : "0";

		/// <summary>The column name, if it is a mapped property of <typeparamref name="T"/>; otherwise the call is refused.</summary>
		protected static string Checked(string column)
		{
			if (string.IsNullOrWhiteSpace(column) || !Mapped.Contains(column))
				throw new ArgumentException($"'{column}' is not a column of {Table}.", nameof(column));
			return column;
		}

		protected static bool IsStringColumn(string column) => Types.TryGetValue(column, out var type) && type == typeof(string);

		public Task<T> GetForDepartmentAsync(int departmentId, string id, bool includeDeleted = false, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(id))
				return Task.FromResult<T>(null);
			var live = SoftDeletable && !includeDeleted ? $" AND {Col("IsDeleted")} = {False}" : string.Empty;
			return QueryFirstOrDefaultAsync<T>($"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(Key)} = {CaseInsensitive("Id")}{live}",
				new { DepartmentId = departmentId, Id = id }, cancellationToken);
		}

		public Task<IEnumerable<T>> GetByIdsAsync(int departmentId, IEnumerable<string> ids, CancellationToken cancellationToken = default)
		{
			var list = (ids ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (list.Length == 0)
				return Task.FromResult<IEnumerable<T>>(new List<T>());
			return QueryAsync<T>($"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList(Key, "Ids")}",
				new { DepartmentId = departmentId, Ids = list }, cancellationToken);
		}

		public Task<IEnumerable<T>> ListAsync(int departmentId, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
		{
			query ??= new IncidentLedgerQuery();
			var parameters = new DynamicParameters();
			var where = Where(departmentId, query, parameters);
			var order = query.OrderBy != null ? Col(Checked(query.OrderBy)) + (query.Descending ? " DESC" : " ASC") : Col("AddedOn") + " DESC";
			if (query.ThenBy != null)
				order += ", " + Col(Checked(query.ThenBy));
			order += ", " + Col(Key);
			parameters.Add("Skip", Math.Max(0, query.Skip));
			parameters.Add("Take", Math.Clamp(query.Take, 1, 5000));
			return QueryAsync<T>($"SELECT * FROM {Tbl(Table)} WHERE {where} ORDER BY {order} {Paging()}", parameters, cancellationToken);
		}

		public Task<int> CountAsync(int departmentId, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			var where = Where(departmentId, query ?? new IncidentLedgerQuery(), parameters);
			return ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {where}", parameters, cancellationToken);
		}

		public async Task<decimal> SumAsync(int departmentId, string column, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			var where = Where(departmentId, query ?? new IncidentLedgerQuery(), parameters);
			return await ScalarAsync<decimal?>($"SELECT SUM({Col(Checked(column))}) FROM {Tbl(Table)} WHERE {where}", parameters, cancellationToken) ?? 0m;
		}

		protected string Where(int departmentId, IncidentLedgerQuery query, DynamicParameters parameters)
		{
			var clauses = new List<string> { $"{Col("DepartmentId")} = {P}DepartmentId" };
			parameters.Add("DepartmentId", departmentId);

			if (SoftDeletable && !query.IncludeDeleted)
				clauses.Add($"{Col("IsDeleted")} = {False}");

			if (HasActivation)
			{
				var ids = query.ActivationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
				if (query.DepartmentCatalogOnly)
					clauses.Add($"{Col("IncidentActivationId")} IS NULL");
				else if (ids != null && ids.Length > 0)
				{
					parameters.Add("ActivationIds", ids);
					var scoped = CaseInsensitiveInList("IncidentActivationId", "ActivationIds");
					clauses.Add(query.IncludeDepartmentCatalog ? $"({scoped} OR {Col("IncidentActivationId")} IS NULL)" : scoped);
				}
			}

			var n = 0;
			foreach (var (column, value) in query.Equal)
			{
				var name = "Eq" + n++;
				if (value == null)
				{
					clauses.Add($"{Col(Checked(column))} IS NULL");
					continue;
				}
				parameters.Add(name, value is DateTime date ? DatabaseTimestamp(date) : value is Enum e ? Convert.ToInt32(e) : value);
				clauses.Add(IsStringColumn(Checked(column)) ? $"{Col(column)} = {CaseInsensitive(name)}" : $"{Col(column)} = {P}{name}");
			}

			foreach (var (column, values) in query.InInts)
			{
				var name = "InI" + n++;
				parameters.Add(name, InListValue(values));
				clauses.Add(values == null || values.Count == 0 ? "1 = 0" : InList(Checked(column), name));
			}

			foreach (var (column, values) in query.InStrings)
			{
				var name = "InS" + n++;
				parameters.Add(name, InListValue(values));
				clauses.Add(values == null || values.Count == 0 ? "1 = 0" : CaseInsensitiveInList(Checked(column), name));
			}

			foreach (var column in query.IsNull)
				clauses.Add($"{Col(Checked(column))} IS NULL");
			foreach (var column in query.IsNotNull)
				clauses.Add($"{Col(Checked(column))} IS NOT NULL");

			if (query.RangeColumn != null)
			{
				if (query.From.HasValue)
				{
					parameters.Add("RangeFrom", DatabaseTimestamp(query.From.Value));
					clauses.Add($"{Col(Checked(query.RangeColumn))} >= {P}RangeFrom");
				}
				if (query.Before.HasValue)
				{
					parameters.Add("RangeBefore", DatabaseTimestamp(query.Before.Value));
					clauses.Add($"{Col(Checked(query.RangeColumn))} < {P}RangeBefore");
				}
			}

			if (!string.IsNullOrWhiteSpace(query.Search) && query.SearchColumns != null && query.SearchColumns.Count > 0)
			{
				var term = query.Search.Trim();
				parameters.Add("Search", "%" + (IsPostgres
					? term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")
					: term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")) + "%");
				var like = IsPostgres ? "ILIKE" : "LIKE";
				clauses.Add("(" + string.Join(" OR ", query.SearchColumns.Select(c => $"{Col(Checked(c))} {like} {P}Search")) + ")");
			}

			return string.Join(" AND ", clauses);
		}

		public async Task<T> InsertNewAsync(T entity, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(entity.IdValue as string))
				entity.IdValue = Guid.NewGuid().ToString("D");
			if (entity.AddedOn == default)
				entity.AddedOn = DateTime.UtcNow;
			entity.AddedByUserId ??= userId;
			if (entity is IIncidentVersioned versioned && versioned.RowVersion < 1)
				versioned.RowVersion = 1;
			return await InsertAsync(entity, cancellationToken, true);
		}

		public async Task<T> UpdateVersionedAsync(T entity, int expectedVersion, string userId, CancellationToken cancellationToken = default)
		{
			if (entity is IIncidentEditable editable)
			{
				editable.EditedOn = DateTime.UtcNow;
				editable.EditedByUserId = userId;
			}

			var owns = UnitOfWork.Transaction == null;
			await UnitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				if (entity is IIncidentVersioned versioned)
				{
					var changed = await ExecuteAsync(
						$"UPDATE {Tbl(Table)} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(Key)} = {CaseInsensitive("Id")} AND {Col("RowVersion")} = {P}Expected",
						new { entity.DepartmentId, Id = entity.IdValue as string, Expected = expectedVersion }, cancellationToken);
					if (changed == 0)
						throw new IncidentConcurrencyException(Table, entity.IdValue as string);
					versioned.RowVersion = expectedVersion + 1;
				}

				var result = await UpdateAsync(entity, cancellationToken, true);
				if (owns) UnitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		public async Task<bool> SoftDeleteAsync(int departmentId, string id, string userId, CancellationToken cancellationToken = default)
		{
			if (!SoftDeletable)
				return false;
			var edited = typeof(IIncidentEditable).IsAssignableFrom(typeof(T))
				? $", {Col("EditedOn")} = {P}Now, {Col("EditedByUserId")} = {P}UserId" : string.Empty;
			var version = Versioned ? $", {Col("RowVersion")} = {Col("RowVersion")} + 1" : string.Empty;
			return await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("IsDeleted")} = {(IsPostgres ? "TRUE" : "1")}{edited}{version} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(Key)} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = id, Now = DatabaseTimestamp(DateTime.UtcNow), UserId = userId }, cancellationToken) > 0;
		}

		public Task<int> PurgeWhereAsync(int departmentId, string column, string value, string dateColumn = null, DateTime? fromDate = null, CancellationToken cancellationToken = default)
		{
			if (SoftDeletable)
				throw new InvalidOperationException($"{Table} is soft-deletable; its rows are never purged.");
			Checked(column);
			var range = dateColumn != null && fromDate.HasValue ? $" AND {Col(Checked(dateColumn))} >= {P}FromDate" : string.Empty;
			var match = IsStringColumn(column) ? $"{Col(column)} = {CaseInsensitive("Value")}" : $"{Col(column)} = {P}Value";
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {match}{range}",
				new { DepartmentId = departmentId, Value = value, FromDate = fromDate?.Date }, cancellationToken);
		}

		public async Task<int> PurgeInAsync(int departmentId, string column, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
		{
			if (SoftDeletable)
				throw new InvalidOperationException($"{Table} is soft-deletable; its rows are never purged.");
			if (!IsStringColumn(Checked(column)))
				throw new ArgumentException($"'{column}' is not a text column of {Table}.", nameof(column));
			var removed = 0;
			// SQL Server expands the list into one parameter per value (2100 at most), so large sets go in chunks.
			foreach (var chunk in (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
				removed += await ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList(column, "Values")}",
					new { DepartmentId = departmentId, Values = chunk }, cancellationToken);
			return removed;
		}

		public Task<T> LockForUpdateAsync(int departmentId, string id, CancellationToken cancellationToken = default)
		{
			if (UnitOfWork?.Transaction == null)
				throw new InvalidOperationException("A transaction is required to lock an Incident Back Office row.");
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(Key)} = {CaseInsensitive("Id")} FOR UPDATE"
				: $"SELECT * FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(Key)} = {CaseInsensitive("Id")}";
			return QueryFirstOrDefaultAsync<T>(sql, new { DepartmentId = departmentId, Id = id }, cancellationToken);
		}
	}
}
