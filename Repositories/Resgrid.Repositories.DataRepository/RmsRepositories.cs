using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Shared plumbing for the Records (RMS) repositories: unit-of-work-aware connection handling, the
	/// dialect ternary (lowercase unquoted identifiers on PostgreSQL, bracketed PascalCase on SQL Server),
	/// and the IN-list form each dialect needs (Dapper does not expand IN @list on Npgsql). Every query
	/// here begins at DepartmentId; there is no ID-only lookup (RMS plan section 5.8).
	/// </summary>
	public abstract class RmsRepositoryBase<T> : RepositoryBase<T> where T : class, IEntity
	{
		protected readonly IConnectionProvider ConnectionProvider;
		protected readonly SqlConfiguration SqlConfiguration;
		protected readonly IUnitOfWork UnitOfWork;

		protected RmsRepositoryBase(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			ConnectionProvider = connectionProvider;
			SqlConfiguration = sqlConfiguration;
			UnitOfWork = unitOfWork;
		}

		protected static bool IsPostgres => DataConfig.DatabaseType == DatabaseTypes.Postgres;

		protected string P => SqlConfiguration.ParameterNotation;

		/// <summary>Schema-qualified table reference in the dialect's identifier style.</summary>
		protected string Tbl(string name)
		{
			return IsPostgres
				? $"{SqlConfiguration.SchemaName}.{name.ToLowerInvariant()}"
				: $"{SqlConfiguration.SchemaName}.[{name}]";
		}

		/// <summary>Column reference in the dialect's identifier style.</summary>
		protected static string Col(string name)
		{
			return IsPostgres ? name.ToLowerInvariant() : $"[{name}]";
		}

		/// <summary>Column list for an explicit SELECT (never SELECT * when a column must be excluded).</summary>
		protected static string Cols(params string[] names)
		{
			return string.Join(", ", names.Select(Col));
		}

		/// <summary>"col IN @Param" on SQL Server; "col = ANY(@Param)" on PostgreSQL.</summary>
		protected string InList(string column, string parameterName, string alias = null)
		{
			var col = alias == null ? Col(column) : alias + "." + Col(column);
			return IsPostgres
				? $"{col} = ANY({P}{parameterName})"
				: $"{col} IN {P}{parameterName}";
		}

		/// <summary>Parameter value for an IN-list: an array on PostgreSQL, an enumerable on SQL Server.</summary>
		protected static object InListValue(IEnumerable<int> values)
		{
			var list = (values ?? Enumerable.Empty<int>()).ToArray();
			return IsPostgres ? (object)list : list.ToList();
		}

		protected static string Concat(params string[] parts)
		{
			return string.Join(IsPostgres ? " || " : " + ", parts);
		}

		protected static string YearOf(string columnExpression)
		{
			return IsPostgres ? $"EXTRACT(YEAR FROM {columnExpression})::int" : $"YEAR({columnExpression})";
		}

		protected static string Paging()
		{
			return IsPostgres ? "OFFSET @Skip LIMIT @Take" : "OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
		}

		protected async Task<TResult> RunAsync<TResult>(Func<DbConnection, Task<TResult>> work, CancellationToken cancellationToken = default)
		{
			try
			{
				if (UnitOfWork?.Connection == null)
				{
					using var connection = ConnectionProvider.Create();
					await connection.OpenAsync(cancellationToken);
					return await work(connection);
				}

				return await work(UnitOfWork.CreateOrGetConnection());
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		protected Task<IEnumerable<TRow>> QueryAsync<TRow>(string sql, object parameters, CancellationToken cancellationToken = default)
		{
			return RunAsync(c => c.QueryAsync<TRow>(new Dapper.CommandDefinition(sql, parameters, UnitOfWork.Transaction, cancellationToken: cancellationToken)), cancellationToken);
		}

		protected async Task<TRow> QueryFirstOrDefaultAsync<TRow>(string sql, object parameters, CancellationToken cancellationToken = default)
		{
			return (await QueryAsync<TRow>(sql, parameters, cancellationToken)).FirstOrDefault();
		}

		protected Task<int> ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken = default)
		{
			return RunAsync(c => c.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, UnitOfWork.Transaction, cancellationToken: cancellationToken)), cancellationToken);
		}

		protected Task<TScalar> ScalarAsync<TScalar>(string sql, object parameters, CancellationToken cancellationToken = default)
		{
			return RunAsync(c => c.ExecuteScalarAsync<TScalar>(new Dapper.CommandDefinition(sql, parameters, UnitOfWork.Transaction, cancellationToken: cancellationToken)), cancellationToken);
		}
	}

	public class RmsOperationalRecordsRepository : RmsRepositoryBase<RmsOperationalRecord>, IRmsOperationalRecordsRepository
	{
		public RmsOperationalRecordsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsOperationalRecord> GetByIdForDepartmentAsync(int departmentId, string recordId)
		{
			return QueryFirstOrDefaultAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOperationalRecordId")} = {P}RecordId",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<RmsOperationalRecord> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey)
		{
			return QueryFirstOrDefaultAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IdempotencyKey")} = {P}Key",
				new { DepartmentId = departmentId, Key = idempotencyKey });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetByCallAsync(int departmentId, int callId)
		{
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")} DESC",
				new { DepartmentId = departmentId, CallId = callId });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetByDefinitionAndStartedRangeAsync(int departmentId, string definitionKey, IEnumerable<int> states, DateTime start, DateTime end)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("DefinitionKey", definitionKey);
			parameters.Add("States", InListValue(states));
			parameters.Add("Start", start);
			parameters.Add("End", end);
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DefinitionKey")} = {P}DefinitionKey AND {InList("State", "States")} AND {Col("StartedOn")} >= {P}Start AND {Col("StartedOn")} <= {P}End AND {Col("DeletedOn")} IS NULL ORDER BY {Col("StartedOn")}",
				parameters);
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetByOwnerAndStatesAsync(int departmentId, string ownerUserId, IEnumerable<int> states)
		{
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OwnerUserId")} = {P}OwnerUserId AND {InList("State", "States")} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("ModifiedOn")} DESC",
				new { DepartmentId = departmentId, OwnerUserId = ownerUserId, States = InListValue(states) });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetByDepartmentAndStatesAsync(int departmentId, IEnumerable<int> states, int? year, int skip, int take)
		{
			var yearClause = year.HasValue ? $" AND {YearOf($"COALESCE({Col("StartedOn")}, {Col("CreatedOn")})")} = {P}Year" : string.Empty;
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")}{yearClause} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")} DESC {Paging()}",
				new { DepartmentId = departmentId, States = InListValue(states), Year = year, Skip = skip, Take = take });
		}

		public Task<int> CountByDepartmentAsync(int departmentId, IEnumerable<int> states)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")} AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, States = InListValue(states) });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetOpenAsync(int departmentId)
		{
			var open = new[] { (int)RmsRecordState.Draft, (int)RmsRecordState.ReadyForReview, (int)RmsRecordState.Returned, (int)RmsRecordState.Approved };
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")} ASC",
				new { DepartmentId = departmentId, States = InListValue(open) });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetFinalizedSinceAsync(int departmentId, DateTime sinceUtc)
		{
			var states = new[] { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended };
			return QueryAsync<RmsOperationalRecord>(
				$"SELECT * FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")} AND {Col("FinalizedOn")} >= {P}Since AND {Col("DeletedOn")} IS NULL ORDER BY {Col("FinalizedOn")} DESC",
				new { DepartmentId = departmentId, States = InListValue(states), Since = sinceUtc });
		}

		public Task<IEnumerable<RmsOperationalRecord>> GetRetentionCandidatesAsync(int departmentId, DateTime cutoffUtc, int take)
		{
			// Closed states only: a Record still being authored or reviewed is never a retention candidate, however old.
			var states = new[] { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Voided, (int)RmsRecordState.Cancelled };
			return QueryAsync<RmsOperationalRecord>(
				$@"SELECT * FROM {Tbl("RmsOperationalRecords")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")}
					AND {Col("FinalizedOn")} IS NOT NULL AND {Col("FinalizedOn")} < {P}Cutoff AND {Col("DeletedOn")} IS NULL
					ORDER BY {Col("FinalizedOn")} {Paging()}",
				new { DepartmentId = departmentId, States = InListValue(states), Cutoff = cutoffUtc, Skip = 0, Take = Math.Clamp(take, 1, 10000) });
		}

		public Task<int> CountAllAsync(int departmentId)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId });
		}

		public Task<int> CountWithoutGroupScopeAsync(int departmentId)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} r WHERE r.{Col("DepartmentId")} = {P}DepartmentId AND r.{Col("DeletedOn")} IS NULL " +
				$"AND NOT EXISTS (SELECT 1 FROM {Tbl("RmsRecordGroupScopes")} s WHERE s.{Col("DepartmentId")} = r.{Col("DepartmentId")} AND s.{Col("RecordId")} = r.{Col("RmsOperationalRecordId")})",
				new { DepartmentId = departmentId });
		}

		public Task<int> CountCreatedSinceAsync(int departmentId, DateTime sinceUtc)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CreatedOn")} >= {P}Since",
				new { DepartmentId = departmentId, Since = sinceUtc });
		}

		public Task<int> CountFinalizedSinceAsync(int departmentId, DateTime sinceUtc)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CreatedOn")} >= {P}Since AND {Col("RevisionCount")} > 0",
				new { DepartmentId = departmentId, Since = sinceUtc });
		}

		public Task<IEnumerable<int>> GetYearsAsync(int departmentId)
		{
			var expr = YearOf($"COALESCE({Col("StartedOn")}, {Col("CreatedOn")})");
			return QueryAsync<int>(
				$"SELECT DISTINCT {expr} AS y FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY y DESC",
				new { DepartmentId = departmentId });
		}

		public async Task<int> GetMaxRecordNumberSequenceAsync(int departmentId, string numberPrefix)
		{
			// Sequences are zero-padded, so the lexicographic MAX is the numeric max.
			var max = await ScalarAsync<string>(
				$"SELECT MAX({Col("RecordNumber")}) FROM {Tbl("RmsOperationalRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordNumber")} LIKE {P}Pattern",
				new { DepartmentId = departmentId, Pattern = numberPrefix + "%" });

			if (string.IsNullOrWhiteSpace(max) || max.Length <= numberPrefix.Length)
				return 0;

			return int.TryParse(max.Substring(numberPrefix.Length), out var sequence) ? sequence : 0;
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string recordId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var affected = await ExecuteAsync(
				$"UPDATE {Tbl("RmsOperationalRecords")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOperationalRecordId")} = {P}RecordId AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, RecordId = recordId, Expected = expectedRowVersion, Now = DateTime.UtcNow }, cancellationToken);

			return affected == 1;
		}
	}

	public class RmsOperationalRecordDetailsRepository : RmsRepositoryBase<RmsOperationalRecordDetail>, IRmsOperationalRecordDetailsRepository
	{
		public RmsOperationalRecordDetailsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsOperationalRecordDetail>> GetDraftsForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
			if (ids.Count == 0)
				return Task.FromResult<IEnumerable<RmsOperationalRecordDetail>>(new List<RmsOperationalRecordDetail>());

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", IsPostgres ? (object)ids.ToArray() : ids);
			return QueryAsync<RmsOperationalRecordDetail>(
				$"SELECT * FROM {Tbl("RmsOperationalRecordDetails")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")} AND {Col("RevisionId")} IS NULL",
				parameters);
		}

		public Task<RmsOperationalRecordDetail> GetDraftAsync(int departmentId, string recordId)
		{
			return QueryFirstOrDefaultAsync<RmsOperationalRecordDetail>(
				$"SELECT * FROM {Tbl("RmsOperationalRecordDetails")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<RmsOperationalRecordDetail> GetByRevisionAsync(int departmentId, string recordId, string revisionId)
		{
			return QueryFirstOrDefaultAsync<RmsOperationalRecordDetail>(
				$"SELECT * FROM {Tbl("RmsOperationalRecordDetails")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} = {P}RevisionId",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}
	}

	public class RmsRecordParticipantsRepository : RmsRepositoryBase<RmsRecordParticipant>, IRmsRecordParticipantsRepository
	{
		public RmsRecordParticipantsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordParticipant>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
			if (ids.Count == 0)
				return Task.FromResult<IEnumerable<RmsRecordParticipant>>(new List<RmsRecordParticipant>());

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", IsPostgres ? (object)ids.ToArray() : ids);
			return QueryAsync<RmsRecordParticipant>(
				$"SELECT * FROM {Tbl("RmsRecordParticipants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")} AND {Col("RevisionId")} IS NULL AND {Col("DeletedOn")} IS NULL ORDER BY {Col("RecordId")}, {Col("Ordinal")}",
				parameters);
		}

		public Task<IEnumerable<RmsRecordParticipant>> GetForRecordAsync(int departmentId, string recordId, string revisionId)
		{
			var revisionClause = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<RmsRecordParticipant>(
				$"SELECT * FROM {Tbl("RmsRecordParticipants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revisionClause} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Ordinal")}",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<IEnumerable<RmsRecordParticipant>> GetByUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsRecordParticipant>(
				$"SELECT * FROM {Tbl("RmsRecordParticipants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId AND {Col("RevisionId")} IS NULL AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("RmsRecordParticipants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);
		}
	}

	public class RmsRecordUnitResponsesRepository : RmsRepositoryBase<RmsRecordUnitResponse>, IRmsRecordUnitResponsesRepository
	{
		public RmsRecordUnitResponsesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordUnitResponse>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
			if (ids.Count == 0)
				return Task.FromResult<IEnumerable<RmsRecordUnitResponse>>(new List<RmsRecordUnitResponse>());

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", IsPostgres ? (object)ids.ToArray() : ids);
			return QueryAsync<RmsRecordUnitResponse>(
				$"SELECT * FROM {Tbl("RmsRecordUnitResponses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")} AND {Col("RevisionId")} IS NULL AND {Col("DeletedOn")} IS NULL ORDER BY {Col("RecordId")}",
				parameters);
		}

		public Task<IEnumerable<RmsRecordUnitResponse>> GetForRecordAsync(int departmentId, string recordId, string revisionId)
		{
			var revisionClause = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<RmsRecordUnitResponse>(
				$"SELECT * FROM {Tbl("RmsRecordUnitResponses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revisionClause} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Ordinal")}",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<IEnumerable<RmsRecordUnitResponse>> GetByUnitAsync(int departmentId, int unitId)
		{
			return QueryAsync<RmsRecordUnitResponse>(
				$"SELECT * FROM {Tbl("RmsRecordUnitResponses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UnitId")} = {P}UnitId AND {Col("RevisionId")} IS NULL AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, UnitId = unitId });
		}

		public Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("RmsRecordUnitResponses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);
		}
	}

	public class RmsRecordAttachmentsRepository : RmsRepositoryBase<RmsRecordAttachment>, IRmsRecordAttachmentsRepository
	{
		private static readonly string[] MetadataColumns =
		{
			"RmsRecordAttachmentId", "DepartmentId", "ProtectionId", "RecordId", "FileName", "ContentType", "ByteSize", "Checksum",
			"StorageReference", "Description", "UploadedByUserId", "UploadedOn", "ScanState", "MetadataStripped", "IsProtected",
			"ProtectedCatalogVersion", "CreatedOn", "ModifiedOn", "RowVersion", "DeletedOn"
		};

		public RmsRecordAttachmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordAttachment>> GetMetadataForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordAttachment>(
				$"SELECT {Cols(MetadataColumns)} FROM {Tbl("RmsRecordAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("UploadedOn")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<IEnumerable<RmsRecordAttachment>> GetPendingScanAsync(int departmentId, int take)
		{
			// Metadata only: the rescan loads bytes one attachment at a time so a sweep never holds a batch of them.
			return QueryAsync<RmsRecordAttachment>(
				$@"SELECT {Cols(MetadataColumns)} FROM {Tbl("RmsRecordAttachments")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ScanState")} = {P}ScanState AND {Col("DeletedOn")} IS NULL
					ORDER BY {Col("UploadedOn")} {Paging()}",
				new { DepartmentId = departmentId, ScanState = (int)RmsAttachmentScanState.Pending, Skip = 0, Take = Math.Clamp(take, 1, 1000) });
		}

		public Task<RmsRecordAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId)
		{
			return QueryFirstOrDefaultAsync<RmsRecordAttachment>(
				$"SELECT * FROM {Tbl("RmsRecordAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordAttachmentId")} = {P}AttachmentId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, AttachmentId = attachmentId });
		}
	}

	public class RmsExternalReferencesRepository : RmsRepositoryBase<RmsExternalReference>, IRmsExternalReferencesRepository
	{
		public RmsExternalReferencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsExternalReference>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsExternalReference>(
				$"SELECT * FROM {Tbl("RmsExternalReferences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CapturedOn")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}
	}

	public class DomainEventOutboxRepository : RmsRepositoryBase<DomainEventOutboxEntry>, IDomainEventOutboxRepository
	{
		public DomainEventOutboxRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<long> GetNextSequenceAsync(int departmentId, string aggregateId)
		{
			return ScalarAsync<long>(
				$"SELECT COALESCE(MAX({Col("Sequence")}), 0) + 1 FROM {Tbl("DomainEventOutbox")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}AggregateId",
				new { DepartmentId = departmentId, AggregateId = aggregateId });
		}

		public Task<IEnumerable<DomainEventOutboxEntry>> ClaimPendingBatchAsync(string leaseOwner, TimeSpan leaseDuration, int batchSize, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var until = utcNow.Add(leaseDuration);
			var pending = (int)DomainEventOutboxState.Pending;

			var sql = IsPostgres
				? $"UPDATE {Tbl("DomainEventOutbox")} SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				  $"WHERE {Col("DomainEventOutboxId")} IN (SELECT {Col("DomainEventOutboxId")} FROM {Tbl("DomainEventOutbox")} WHERE {Col("State")} = {P}Pending " +
				  $"AND ({Col("NextAttemptOn")} IS NULL OR {Col("NextAttemptOn")} <= {P}Now) AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now) " +
				  $"ORDER BY {Col("DomainEventOutboxId")} LIMIT {P}BatchSize FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"UPDATE TOP ({P}BatchSize) {Tbl("DomainEventOutbox")} WITH (READPAST) SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				  $"OUTPUT inserted.* WHERE {Col("State")} = {P}Pending AND ({Col("NextAttemptOn")} IS NULL OR {Col("NextAttemptOn")} <= {P}Now) AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now)";

			return QueryAsync<DomainEventOutboxEntry>(sql, new { Owner = leaseOwner, Until = until, Now = utcNow, BatchSize = batchSize, Pending = pending }, cancellationToken);
		}

		public async Task<DomainEventOutboxEntry> ClaimByIdAsync(long domainEventOutboxId, string leaseOwner, TimeSpan leaseDuration, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var until = utcNow.Add(leaseDuration);
			var pending = (int)DomainEventOutboxState.Pending;
			var sql = IsPostgres
				? $"UPDATE {Tbl("DomainEventOutbox")} SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("Attempts")} = {Col("Attempts")} + 1 WHERE {Col("DomainEventOutboxId")} = {P}Id AND {Col("State")} = {P}Pending AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now) RETURNING *"
				: $"UPDATE {Tbl("DomainEventOutbox")} SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("Attempts")} = {Col("Attempts")} + 1 OUTPUT inserted.* WHERE {Col("DomainEventOutboxId")} = {P}Id AND {Col("State")} = {P}Pending AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now)";

			return await QueryFirstOrDefaultAsync<DomainEventOutboxEntry>(sql, new { Id = domainEventOutboxId, Owner = leaseOwner, Until = until, Now = utcNow, Pending = pending }, cancellationToken);
		}

		public async Task<bool> MarkDispatchedAsync(long domainEventOutboxId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var affected = await ExecuteAsync(
				$"UPDATE {Tbl("DomainEventOutbox")} SET {Col("State")} = {P}State, {Col("DispatchedOn")} = {P}Now, {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL, {Col("LastError")} = NULL WHERE {Col("DomainEventOutboxId")} = {P}Id",
				new { Id = domainEventOutboxId, Now = utcNow, State = (int)DomainEventOutboxState.Dispatched }, cancellationToken);
			return affected == 1;
		}

		public async Task<bool> MarkFailedAsync(long domainEventOutboxId, string error, DateTime? nextAttemptOn, bool terminal, CancellationToken cancellationToken = default)
		{
			var state = terminal ? (int)DomainEventOutboxState.Failed : (int)DomainEventOutboxState.Pending;
			var affected = await ExecuteAsync(
				$"UPDATE {Tbl("DomainEventOutbox")} SET {Col("State")} = {P}State, {Col("NextAttemptOn")} = {P}Next, {Col("LastError")} = {P}Error, {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL WHERE {Col("DomainEventOutboxId")} = {P}Id",
				new { Id = domainEventOutboxId, State = state, Next = nextAttemptOn, Error = error }, cancellationToken);
			return affected == 1;
		}

		public Task<int> CountByStateAsync(int state)
		{
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("DomainEventOutbox")} WHERE {Col("State")} = {P}State", new { State = state });
		}

		public Task<DateTime?> GetOldestPendingCreatedOnAsync()
		{
			return ScalarAsync<DateTime?>($"SELECT MIN({Col("CreatedOn")}) FROM {Tbl("DomainEventOutbox")} WHERE {Col("State")} = {P}State", new { State = (int)DomainEventOutboxState.Pending });
		}

		public Task<int> PurgeDispatchedOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("DomainEventOutbox")} WHERE {Col("State")} = {P}State AND {Col("DispatchedOn")} < {P}Cutoff",
				new { State = (int)DomainEventOutboxState.Dispatched, Cutoff = cutoffUtc }, cancellationToken);
		}
	}

	public class RmsDepartmentCutoversRepository : RmsRepositoryBase<RmsDepartmentCutover>, IRmsDepartmentCutoversRepository
	{
		public RmsDepartmentCutoversRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsDepartmentCutover>> GetActiveAsync()
		{
			return QueryAsync<RmsDepartmentCutover>(
				$"SELECT * FROM {Tbl("RmsDepartmentCutovers")} WHERE {Col("State")} = {P}State ORDER BY {Col("DepartmentId")}",
				new { State = (int)RmsDepartmentCutoverState.Active });
		}

		public Task<RmsDepartmentCutover> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<RmsDepartmentCutover>(
				$"SELECT * FROM {Tbl("RmsDepartmentCutovers")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}
	}

	public class RmsDepartmentCutoverEventsRepository : RmsRepositoryBase<RmsDepartmentCutoverEvent>, IRmsDepartmentCutoverEventsRepository
	{
		public RmsDepartmentCutoverEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsDepartmentCutoverEvent>> GetForCutoverAsync(int departmentId, int cutoverId)
		{
			return QueryAsync<RmsDepartmentCutoverEvent>(
				$"SELECT * FROM {Tbl("RmsDepartmentCutoverEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsDepartmentCutoverId")} = {P}CutoverId ORDER BY {Col("OccurredOn")}",
				new { DepartmentId = departmentId, CutoverId = cutoverId });
		}
	}

	public class RmsRevisionsRepository : RmsRepositoryBase<RmsRevision>, IRmsRevisionsRepository
	{
		public RmsRevisionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRevision>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRevision>(
				$"SELECT * FROM {Tbl("RmsRevisions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("RevisionNumber")} DESC",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<RmsRevision> GetByIdForDepartmentAsync(int departmentId, string revisionId)
		{
			return QueryFirstOrDefaultAsync<RmsRevision>(
				$"SELECT * FROM {Tbl("RmsRevisions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRevisionId")} = {P}RevisionId",
				new { DepartmentId = departmentId, RevisionId = revisionId });
		}
	}

	public class RmsAccessAuditsRepository : RmsRepositoryBase<RmsAccessAudit>, IRmsAccessAuditsRepository
	{
		public RmsAccessAuditsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsAccessAudit>> GetForRecordAsync(int departmentId, string recordId, int take)
		{
			return QueryAsync<RmsAccessAudit>(
				$"SELECT * FROM {Tbl("RmsAccessAudits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("OccurredOn")} DESC {Paging()}",
				new { DepartmentId = departmentId, RecordId = recordId, Skip = 0, Take = take });
		}
	}

	public class RmsRecordSearchProjectionsRepository : RmsRepositoryBase<RmsRecordSearchProjection>, IRmsRecordSearchProjectionsRepository
	{
		public RmsRecordSearchProjectionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordSearchProjection> GetByRecordIdAsync(int departmentId, string recordId)
		{
			return QueryFirstOrDefaultAsync<RmsRecordSearchProjection>(
				$"SELECT * FROM {Tbl("RmsRecordSearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordSearchProjectionId")} = {P}RecordId",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<IEnumerable<RmsRecordSearchProjection>> QueryAsync(int departmentId, RmsRecordQuery query)
		{
			var (where, parameters) = BuildWhere(departmentId, query);
			return QueryAsync<RmsRecordSearchProjection>(
				$"SELECT p.* FROM {Tbl("RmsRecordSearchProjections")} p WHERE {where} ORDER BY COALESCE(p.{Col("OccurredOn")}, p.{Col("RecordCreatedOn")}) DESC {Paging()}",
				parameters);
		}

		public Task<int> CountAsync(int departmentId, RmsRecordQuery query)
		{
			var (where, parameters) = BuildWhere(departmentId, query);
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsRecordSearchProjections")} p WHERE {where}", parameters);
		}

		public Task<IEnumerable<int>> GetYearsAsync(int departmentId)
		{
			var expr = YearOf($"COALESCE({Col("OccurredOn")}, {Col("RecordCreatedOn")})");
			return QueryAsync<int>(
				$"SELECT DISTINCT {expr} AS y FROM {Tbl("RmsRecordSearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY y DESC",
				new { DepartmentId = departmentId });
		}

		/// <summary>
		/// Visibility is a join, never a post-filter, so paging counts cannot leak (plan section 5.7.1).
		/// The always-visible cases (author, owner, reviewer, named participant) resolve inside the query.
		/// </summary>
		public Task<IEnumerable<RmsRecordSearchProjection>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take, string sinceId = null)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 500 : Math.Min(take, 5000));
			var sinceClause = string.Empty;
			if (since.HasValue)
			{
				// The predicate has to match the ordering, tie-breaker included, or the rows that share the cursor's
				// timestamp and sort after it are dropped on the next page.
				if (string.IsNullOrWhiteSpace(sinceId))
				{
					sinceClause = $" AND {Col("ModifiedOn")} > {P}Since";
				}
				else
				{
					sinceClause = $" AND ({Col("ModifiedOn")} > {P}Since OR ({Col("ModifiedOn")} = {P}Since AND {Col("RmsRecordSearchProjectionId")} > {P}SinceId))";
					parameters.Add("SinceId", sinceId);
				}

				parameters.Add("Since", since.Value);
			}

			return QueryAsync<RmsRecordSearchProjection>(
				$"SELECT * FROM {Tbl("RmsRecordSearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId{sinceClause} ORDER BY {Col("ModifiedOn")} ASC, {Col("RmsRecordSearchProjectionId")} ASC {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsRecordSearchProjection>> GetByIdsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
			if (ids.Count == 0)
				return Task.FromResult<IEnumerable<RmsRecordSearchProjection>>(new List<RmsRecordSearchProjection>());

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", IsPostgres ? (object)ids.ToArray() : ids);
			return QueryAsync<RmsRecordSearchProjection>(
				$"SELECT * FROM {Tbl("RmsRecordSearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {InList("RmsRecordSearchProjectionId", "Ids")}",
				parameters);
		}

		private (string, DynamicParameters) BuildWhere(int departmentId, RmsRecordQuery query)
		{
			query = query ?? new RmsRecordQuery();
			var sb = new StringBuilder();
			var parameters = new DynamicParameters();

			sb.Append($"p.{Col("DepartmentId")} = {P}DepartmentId AND p.{Col("DeletedOn")} IS NULL");
			parameters.Add("DepartmentId", departmentId);

			if (!query.IncludeLegacy)
			{
				sb.Append($" AND p.{Col("IsLegacy")} = {P}NotLegacy");
				parameters.Add("NotLegacy", false);
			}

			if (query.States != null && query.States.Count > 0)
			{
				sb.Append($" AND {InList("State", "States", "p")}");
				parameters.Add("States", InListValue(query.States));
			}

			if (!string.IsNullOrWhiteSpace(query.DefinitionKey))
			{
				sb.Append($" AND p.{Col("DefinitionKey")} = {P}DefinitionKey");
				parameters.Add("DefinitionKey", query.DefinitionKey);
			}

			if (query.Year.HasValue)
			{
				sb.Append($" AND {YearOf($"COALESCE(p.{Col("OccurredOn")}, p.{Col("RecordCreatedOn")})")} = {P}Year");
				parameters.Add("Year", query.Year.Value);
			}

			if (query.CallId.HasValue)
			{
				sb.Append($" AND p.{Col("CallId")} = {P}CallId");
				parameters.Add("CallId", query.CallId.Value);
			}

			if (!string.IsNullOrWhiteSpace(query.AuthorUserId))
			{
				sb.Append($" AND p.{Col("AuthorUserId")} = {P}AuthorUserId");
				parameters.Add("AuthorUserId", query.AuthorUserId);
			}

			if (!string.IsNullOrWhiteSpace(query.OwnerUserId))
			{
				sb.Append($" AND p.{Col("OwnerUserId")} = {P}OwnerUserId");
				parameters.Add("OwnerUserId", query.OwnerUserId);
			}

			if (query.StationGroupId.HasValue)
			{
				sb.Append($" AND p.{Col("StationGroupId")} = {P}StationGroupId");
				parameters.Add("StationGroupId", query.StationGroupId.Value);
			}

			if (query.VisibleGroupIds != null)
			{
				var viewer = query.ViewerUserId ?? string.Empty;
				var participantCsv = Concat("','", $"COALESCE(p.{Col("ParticipantUserIds")}, '')", "','");
				sb.Append(" AND (")
					.Append($"p.{Col("AuthorUserId")} = {P}Viewer OR p.{Col("OwnerUserId")} = {P}Viewer OR p.{Col("ReviewerUserId")} = {P}Viewer")
					.Append($" OR ({participantCsv}) LIKE {P}ViewerCsvPattern")
					.Append($" OR EXISTS (SELECT 1 FROM {Tbl("RmsRecordGroupScopes")} s WHERE s.{Col("DepartmentId")} = p.{Col("DepartmentId")} AND s.{Col("RecordId")} = p.{Col("RmsRecordSearchProjectionId")} AND {InList("DepartmentGroupId", "VisibleGroupIds", "s")})")
					.Append(")");
				parameters.Add("Viewer", viewer);
				parameters.Add("ViewerCsvPattern", "%," + viewer + ",%");
				parameters.Add("VisibleGroupIds", InListValue(query.VisibleGroupIds));
			}

			parameters.Add("Skip", query.Skip);
			parameters.Add("Take", query.Take <= 0 ? 50 : Math.Min(query.Take, 500));

			return (sb.ToString(), parameters);
		}
	}

	public class RmsSearchIndexStatesRepository : RmsRepositoryBase<RmsSearchIndexState>, IRmsSearchIndexStatesRepository
	{
		public RmsSearchIndexStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsSearchIndexState> GetAsync(int departmentId, string indexName)
		{
			return QueryFirstOrDefaultAsync<RmsSearchIndexState>(
				$"SELECT * FROM {Tbl("RmsSearchIndexStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IndexName")} = {P}IndexName",
				new { DepartmentId = departmentId, IndexName = indexName });
		}
	}

	public class RmsRecordGroupScopesRepository : RmsRepositoryBase<RmsRecordGroupScope>, IRmsRecordGroupScopesRepository
	{
		public RmsRecordGroupScopesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private sealed class GroupCountRow
		{
			public int DepartmentGroupId { get; set; }
			public int RecordCount { get; set; }
		}

		public async Task<IDictionary<int, int>> CountRecordsByGroupAsync(int departmentId)
		{
			var rows = await QueryAsync<GroupCountRow>(
				$"SELECT s.{Col("DepartmentGroupId")} AS {Col("DepartmentGroupId")}, COUNT(DISTINCT s.{Col("RecordId")}) AS {Col("RecordCount")} " +
				$"FROM {Tbl("RmsRecordGroupScopes")} s INNER JOIN {Tbl("RmsOperationalRecords")} r ON r.{Col("RmsOperationalRecordId")} = s.{Col("RecordId")} AND r.{Col("DepartmentId")} = s.{Col("DepartmentId")} " +
				$"WHERE s.{Col("DepartmentId")} = {P}DepartmentId AND r.{Col("DeletedOn")} IS NULL GROUP BY s.{Col("DepartmentGroupId")}",
				new { DepartmentId = departmentId });

			return (rows ?? Enumerable.Empty<GroupCountRow>()).ToDictionary(x => x.DepartmentGroupId, x => x.RecordCount);
		}

		public Task<IEnumerable<RmsRecordGroupScope>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = (recordIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
			if (ids.Count == 0)
				return Task.FromResult<IEnumerable<RmsRecordGroupScope>>(new List<RmsRecordGroupScope>());

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", IsPostgres ? (object)ids.ToArray() : ids);
			return QueryAsync<RmsRecordGroupScope>(
				$"SELECT * FROM {Tbl("RmsRecordGroupScopes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")}",
				parameters);
		}

		public Task<IEnumerable<RmsRecordGroupScope>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordGroupScope>(
				$"SELECT * FROM {Tbl("RmsRecordGroupScopes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public async Task ReplaceForRecordAsync(int departmentId, string recordId, IEnumerable<RmsRecordGroupScope> scopes, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync(
				$"DELETE FROM {Tbl("RmsRecordGroupScopes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId",
				new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);

			foreach (var scope in scopes ?? Enumerable.Empty<RmsRecordGroupScope>())
			{
				scope.DepartmentId = departmentId;
				scope.RecordId = recordId;
				if (scope.CreatedOn == default)
					scope.CreatedOn = DateTime.UtcNow;

				await InsertAsync(scope, cancellationToken, true);
			}
		}
	}

	public class RmsRecordSharesRepository : RmsRepositoryBase<RmsRecordShare>, IRmsRecordSharesRepository
	{
		public RmsRecordSharesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsRecordShare>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordShare>(
				$"SELECT * FROM {Tbl("RmsRecordShares")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("GrantedOn")} DESC",
				new { DepartmentId = departmentId, RecordId = recordId });
		}
	}

	/// <summary>
	/// Immutable evidence artifacts (registry M0169, RMS-3). No update path: a correction supersedes rather than
	/// edits, so a stored checksum keeps meaning what it meant when it was attested to.
	/// </summary>
	public class RmsEvidenceArtifactsRepository : RmsRepositoryBase<RmsEvidenceArtifact>, IRmsEvidenceArtifactsRepository
	{
		public RmsEvidenceArtifactsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsEvidenceArtifact> GetByIdForDepartmentAsync(int departmentId, string artifactId)
		{
			return QueryFirstOrDefaultAsync<RmsEvidenceArtifact>(
				$"SELECT * FROM {Tbl("RmsEvidenceArtifacts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsEvidenceArtifactId")} = {P}ArtifactId",
				new { DepartmentId = departmentId, ArtifactId = artifactId });
		}

		public Task<IEnumerable<RmsEvidenceArtifact>> GetForRecordAsync(int departmentId, string recordId, string revisionId, bool includeSuperseded)
		{
			var revisionClause = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			var supersededClause = includeSuperseded ? string.Empty : $" AND {Col("SupersededOn")} IS NULL";
			return QueryAsync<RmsEvidenceArtifact>(
				$@"SELECT * FROM {Tbl("RmsEvidenceArtifacts")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revisionClause}
					AND {Col("DeletedOn")} IS NULL{supersededClause}
					ORDER BY {Col("Kind")}, {Col("CapturedOn")}",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<RmsEvidenceArtifact> GetCurrentDraftOfKindAsync(int departmentId, string recordId, RmsEvidenceKind kind, string sourceEntityId)
		{
			// A source-scoped kind (a promoted chat set, one run card activation) can have several current
			// artifacts, one per source row; a whole-record kind has at most one.
			var sourceClause = sourceEntityId == null ? string.Empty : $" AND {Col("SourceEntityId")} = {P}SourceEntityId";
			return QueryFirstOrDefaultAsync<RmsEvidenceArtifact>(
				$@"SELECT * FROM {Tbl("RmsEvidenceArtifacts")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL
					AND {Col("Kind")} = {P}Kind AND {Col("SupersededOn")} IS NULL AND {Col("DeletedOn")} IS NULL{sourceClause}",
				new { DepartmentId = departmentId, RecordId = recordId, Kind = (int)kind, SourceEntityId = sourceEntityId });
		}

		public Task<int> BindDraftToRevisionAsync(int departmentId, string recordId, string revisionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$@"UPDATE {Tbl("RmsEvidenceArtifacts")} SET {Col("RevisionId")} = {P}RevisionId, {Col("ModifiedOn")} = {P}Now
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId, Now = utcNow }, cancellationToken);
		}

		public Task<int> CountForRecordAsync(int departmentId, string recordId)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsEvidenceArtifacts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId });
		}
	}

	/// <summary>Due state per (Record, obligation) — registry M0170, RMS-3.</summary>
	public class RmsRecordDueStatesRepository : RmsRepositoryBase<RmsRecordDueState>, IRmsRecordDueStatesRepository
	{
		public RmsRecordDueStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordDueState> GetAsync(int departmentId, string recordId, RmsRecordObligation obligation)
		{
			return QueryFirstOrDefaultAsync<RmsRecordDueState>(
				$"SELECT * FROM {Tbl("RmsRecordDueStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("Obligation")} = {P}Obligation",
				new { DepartmentId = departmentId, RecordId = recordId, Obligation = (int)obligation });
		}

		public Task<IEnumerable<RmsRecordDueState>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordDueState>(
				$"SELECT * FROM {Tbl("RmsRecordDueStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("Obligation")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<IEnumerable<RmsRecordDueState>> GetOpenForDepartmentAsync(int departmentId, int take)
		{
			return QueryAsync<RmsRecordDueState>(
				$@"SELECT * FROM {Tbl("RmsRecordDueStates")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LastEmittedState")} <> {P}Cleared
					ORDER BY {Col("DueOn")} {Paging()}",
				new { DepartmentId = departmentId, Cleared = (int)RmsDueState.Cleared, Skip = 0, Take = Math.Clamp(take, 1, 10000) });
		}

		public Task<int> CountOverdueAsync(int departmentId)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsRecordDueStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LastEmittedState")} = {P}Overdue",
				new { DepartmentId = departmentId, Overdue = (int)RmsDueState.Overdue });
		}

		public Task<int> ClearForRecordAsync(int departmentId, string recordId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$@"UPDATE {Tbl("RmsRecordDueStates")} SET {Col("LastEmittedState")} = {P}Cleared, {Col("ModifiedOn")} = {P}Now
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("LastEmittedState")} <> {P}Cleared",
				new { DepartmentId = departmentId, RecordId = recordId, Cleared = (int)RmsDueState.Cleared, Now = utcNow }, cancellationToken);
		}
	}

	/// <summary>Public-records requests — registry M0171, RMS-3.</summary>
	public class RmsDisclosureRequestsRepository : RmsRepositoryBase<RmsDisclosureRequest>, IRmsDisclosureRequestsRepository
	{
		public RmsDisclosureRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsDisclosureRequest> GetByIdForDepartmentAsync(int departmentId, string requestId)
		{
			return QueryFirstOrDefaultAsync<RmsDisclosureRequest>(
				$"SELECT * FROM {Tbl("RmsDisclosureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsDisclosureRequestId")} = {P}RequestId",
				new { DepartmentId = departmentId, RequestId = requestId });
		}

		public Task<IEnumerable<RmsDisclosureRequest>> GetForDepartmentAsync(int departmentId, IEnumerable<int> states, int skip, int take)
		{
			var stateList = states?.ToList();
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", Math.Max(0, skip));
			parameters.Add("Take", Math.Clamp(take, 1, 1000));

			var where = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL";
			if (stateList != null && stateList.Count > 0)
			{
				where += $" AND {InList("State", "States")}";
				parameters.Add("States", InListValue(stateList));
			}

			// Oldest deadline first: the request closest to breaching its statutory clock is the one that matters.
			return QueryAsync<RmsDisclosureRequest>(
				$"SELECT * FROM {Tbl("RmsDisclosureRequests")} WHERE {where} ORDER BY {Col("StatutoryDueOn")}, {Col("ReceivedOn")} {Paging()}", parameters);
		}

		public Task<int> CountByStateAsync(int departmentId, RmsDisclosureState state)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsDisclosureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, State = (int)state });
		}

		public Task<int> CountOverdueAsync(int departmentId, DateTime utcNow)
		{
			return ScalarAsync<int>(
				$@"SELECT COUNT(1) FROM {Tbl("RmsDisclosureRequests")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("ClosedOn")} IS NULL
					AND {Col("StatutoryDueOn")} IS NOT NULL AND {Col("StatutoryDueOn")} < {P}Now",
				new { DepartmentId = departmentId, Now = utcNow });
		}

		public Task<int> GetMaxRequestNumberSequenceAsync(int departmentId, string numberPrefix)
		{
			return ScalarAsync<int>(
				IsPostgres
					? $"SELECT COALESCE(MAX(CAST(SUBSTRING({Col("RequestNumber")} FROM '[0-9]+$') AS INTEGER)), 0) FROM {Tbl("RmsDisclosureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RequestNumber")} LIKE {P}Prefix"
					: $"SELECT ISNULL(MAX(CAST(RIGHT({Col("RequestNumber")}, CHARINDEX('-', REVERSE({Col("RequestNumber")})) - 1) AS INT)), 0) FROM {Tbl("RmsDisclosureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RequestNumber")} LIKE {P}Prefix",
				new { DepartmentId = departmentId, Prefix = numberPrefix + "%" });
		}
	}

	/// <summary>Immutable produced sets — registry M0171, RMS-3.</summary>
	public class RmsDisclosureProductionsRepository : RmsRepositoryBase<RmsDisclosureProduction>, IRmsDisclosureProductionsRepository
	{
		public RmsDisclosureProductionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsDisclosureProduction> GetByIdForDepartmentAsync(int departmentId, string productionId)
		{
			return QueryFirstOrDefaultAsync<RmsDisclosureProduction>(
				$"SELECT * FROM {Tbl("RmsDisclosureProductions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsDisclosureProductionId")} = {P}ProductionId",
				new { DepartmentId = departmentId, ProductionId = productionId });
		}

		public Task<IEnumerable<RmsDisclosureProduction>> GetForRequestAsync(int departmentId, string requestId)
		{
			return QueryAsync<RmsDisclosureProduction>(
				$"SELECT * FROM {Tbl("RmsDisclosureProductions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DisclosureRequestId")} = {P}RequestId ORDER BY {Col("ProductionNumber")}",
				new { DepartmentId = departmentId, RequestId = requestId });
		}

		public Task<int> GetMaxProductionNumberAsync(int departmentId, string requestId)
		{
			return ScalarAsync<int>(
				$"SELECT COALESCE(MAX({Col("ProductionNumber")}), 0) FROM {Tbl("RmsDisclosureProductions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DisclosureRequestId")} = {P}RequestId",
				new { DepartmentId = departmentId, RequestId = requestId });
		}
	}

	/// <summary>Legal holds that suspend retention — registry M0170, RMS-3.</summary>
	public class RmsRecordLegalHoldsRepository : RmsRepositoryBase<RmsRecordLegalHold>, IRmsRecordLegalHoldsRepository
	{
		public RmsRecordLegalHoldsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordLegalHold> GetByIdForDepartmentAsync(int departmentId, string holdId)
		{
			return QueryFirstOrDefaultAsync<RmsRecordLegalHold>(
				$"SELECT * FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordLegalHoldId")} = {P}HoldId",
				new { DepartmentId = departmentId, HoldId = holdId });
		}

		public Task<IEnumerable<RmsRecordLegalHold>> GetActiveForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsRecordLegalHold>(
				$"SELECT * FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ReleasedOn")} IS NULL ORDER BY {Col("PlacedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsRecordLegalHold>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordLegalHold>(
				$"SELECT * FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("PlacedOn")} DESC",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<IEnumerable<RmsRecordLegalHold>> GetAllForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsRecordLegalHold>(
				$"SELECT * FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("PlacedOn")} DESC",
				new { DepartmentId = departmentId });
		}
	}
}
