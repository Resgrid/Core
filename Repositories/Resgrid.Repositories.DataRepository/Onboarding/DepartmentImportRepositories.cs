using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Onboarding
{
	/// <summary>
	/// Shared plumbing for the Department Import repositories (M0273–M0276): department-scoped reads by id, batched
	/// multi-row inserts for staging, and the dialect helpers of <see cref="RmsRepositoryBase{T}"/>. Ids are citext on
	/// PostgreSQL, so every equality against a key goes through CaseInsensitive.
	/// </summary>
	public abstract class ImportRepositoryBase<T> : RmsRepositoryBase<T>, IImportRowRepository<T> where T : ImportRow
	{
		// SQL Server allows 2,100 parameters per statement; stay well under it.
		private const int MaxParametersPerStatement = 2000;
		private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ColumnCache = new ConcurrentDictionary<Type, PropertyInfo[]>();

		protected ImportRepositoryBase(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected static string Table => DepartmentImportTables.All[typeof(T)];

		protected string Select => $"SELECT * FROM {Tbl(Table)}";

		protected string ByDepartment => $"{Col("DepartmentId")} = {P}DepartmentId";

		public Task<T> GetByIdAsync(int departmentId, string id, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(id))
				return Task.FromResult<T>(null);

			return QueryFirstOrDefaultAsync<T>($"{Select} WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = id }, cancellationToken);
		}

		public async Task InsertManyAsync(IReadOnlyList<T> rows, CancellationToken cancellationToken = default)
		{
			if (rows == null || rows.Count == 0)
				return;

			var columns = ColumnCache.GetOrAdd(typeof(T), type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null)
				.Where(p => !new[] { "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" }.Contains(p.Name))
				.ToArray());

			var rowsPerStatement = Math.Max(1, MaxParametersPerStatement / columns.Length);
			var columnList = string.Join(", ", columns.Select(c => Col(c.Name)));

			for (var offset = 0; offset < rows.Count; offset += rowsPerStatement)
			{
				var batch = rows.Skip(offset).Take(rowsPerStatement).ToList();
				var parameters = new DynamicParameters();
				var sql = new StringBuilder($"INSERT INTO {Tbl(Table)} ({columnList}) VALUES ");

				for (var r = 0; r < batch.Count; r++)
				{
					var row = batch[r];
					if (string.IsNullOrWhiteSpace(row.Id))
						row.Id = Guid.NewGuid().ToString();

					Utf8WriteGuard.Sanitize(row);
					sql.Append(r == 0 ? "(" : ", (");
					for (var c = 0; c < columns.Length; c++)
					{
						var name = $"r{r}c{c}";
						var value = columns[c].GetValue(row);
						if (value is DateTime dt)
							value = DatabaseTimestamp(dt);

						parameters.Add(name, value);
						sql.Append(c == 0 ? string.Empty : ", ").Append(P).Append(name);
					}
					sql.Append(')');
				}

				await ExecuteAsync(sql.ToString(), parameters, cancellationToken);
			}
		}

		protected async Task<List<TRow>> ListAsync<TRow>(string sql, object parameters, CancellationToken cancellationToken)
		{
			var rows = await QueryAsync<TRow>(sql, parameters, cancellationToken);
			return rows?.ToList() ?? new List<TRow>();
		}

		protected string Page(int skip, int take) => IsPostgres
			? $" OFFSET {Math.Max(0, skip)} LIMIT {Math.Max(1, take)}"
			: $" OFFSET {Math.Max(0, skip)} ROWS FETCH NEXT {Math.Max(1, take)} ROWS ONLY";

		protected string Top(int take, string selectList = "*") => IsPostgres
			? $"SELECT {selectList}"
			: $"SELECT TOP ({Math.Max(1, take)}) {selectList}";

		protected string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;

		protected static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}

	/// <summary>Repositories for rows owned by a workspace.</summary>
	public abstract class ImportWorkspaceChildRepositoryBase<T> : ImportRepositoryBase<T>, IImportWorkspaceChildRepository<T> where T : ImportRow
	{
		protected ImportWorkspaceChildRepositoryBase(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected string ByWorkspace => $"{ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")}";

		/// <summary>Order applied by <see cref="GetAllForWorkspaceAsync"/>.</summary>
		protected virtual string WorkspaceOrder => Col("Id");

		public Task<List<T>> GetAllForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ListAsync<T>($"{Select} WHERE {ByWorkspace} ORDER BY {WorkspaceOrder}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<int> DeleteAllForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByWorkspace}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}
	}

	public class ImportWorkspacesRepository : ImportRepositoryBase<ImportWorkspace>, IImportWorkspacesRepository
	{
		public ImportWorkspacesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static readonly int[] OpenStatuses =
		{
			(int)ImportWorkspaceStatus.Draft, (int)ImportWorkspaceStatus.Collecting, (int)ImportWorkspaceStatus.Reviewing,
			(int)ImportWorkspaceStatus.Importing, (int)ImportWorkspaceStatus.Verifying, (int)ImportWorkspaceStatus.ReadyToActivate
		};

		public Task<List<ImportWorkspace>> GetAllForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportWorkspace>($"{Select} WHERE {ByDepartment} ORDER BY {Col("LastActivityOn")} DESC",
				new { DepartmentId = departmentId }, cancellationToken);
		}

		public async Task<bool> UpdateWithRevisionAsync(ImportWorkspace workspace, int expectedRevision, CancellationToken cancellationToken = default)
		{
			var assignments = string.Join(", ", new[]
			{
				"Name", "InternalReference", "Mode", "Status", "OperatingModel", "OwnerUserId", "TargetActivationDate",
				"OperatingProfileRevision", "CatalogVersion", "DefaultWriteMode", "RequireSecondApprover", "DomainPlan",
				"SupportAccessLevel", "SupportAccessExpiresOn", "NeedsEngineering", "EngineeringNote", "UpdatedOn", "LastActivityOn",
				"ActivatedOn", "ClosedOn", "CancelledOn", "AbandonReminderSentOn", "StagingRetentionUntil", "StaffViewDigestSentOn"
			}.Select(c => $"{Col(c)} = {P}{c}"));

			var parameters = new DynamicParameters(workspace);
			parameters.Add("ExpectedRevision", expectedRevision);
			parameters.Add("NewRevision", expectedRevision + 1);
			foreach (var dateColumn in new[] { "UpdatedOn", "LastActivityOn" })
				parameters.Add(dateColumn, DatabaseTimestamp((DateTime)typeof(ImportWorkspace).GetProperty(dateColumn).GetValue(workspace)));

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {assignments}, {Col("Revision")} = {P}NewRevision " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")} AND {Col("Revision")} = {P}ExpectedRevision",
				parameters, cancellationToken);

			if (rows == 1)
				workspace.Revision = expectedRevision + 1;

			return rows == 1;
		}

		public Task TouchAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"UPDATE {Tbl(Table)} SET {Col("LastActivityOn")} = {P}Now WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = workspaceId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<List<ImportWorkspace>> GetIdleOpenWorkspacesAsync(DateTime idleBefore, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportWorkspace>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {InList("Status", "Open")} AND {Col("LastActivityOn")} < {P}Before ORDER BY {Col("LastActivityOn")}{Limit(take)}",
				new { Open = InListValue(OpenStatuses), Before = DatabaseTimestamp(idleBefore) }, cancellationToken);
		}

		public Task<List<ImportWorkspace>> GetQueueAsync(DateTime since, int? departmentId, int skip, int take, CancellationToken cancellationToken = default)
		{
			var department = departmentId.HasValue ? $" AND {ByDepartment}" : string.Empty;
			return ListAsync<ImportWorkspace>(
				$"{Select} WHERE ({InList("Status", "Open")} OR {Col("UpdatedOn")} >= {P}Since){department} " +
				$"ORDER BY {Col("LastActivityOn")} DESC, {Col("Id")}{Page(skip, take)}",
				new { Open = InListValue(OpenStatuses), Since = DatabaseTimestamp(since), DepartmentId = departmentId ?? 0 }, cancellationToken);
		}

		public async Task<ImportWorkspace> FindByIdAnyDepartmentAsync(string workspaceId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(workspaceId))
				return null;
			return (await ListAsync<ImportWorkspace>($"{Select} WHERE {Col("Id")} = {CaseInsensitive("Id")}", new { Id = workspaceId }, cancellationToken)).FirstOrDefault();
		}

		public Task<List<ImportWorkspace>> GetPastStagingRetentionAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportWorkspace>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("StagingRetentionUntil")} IS NOT NULL AND {Col("StagingRetentionUntil")} < {P}Now " +
				$"ORDER BY {Col("StagingRetentionUntil")}{Limit(take)}",
				new { Now = DatabaseTimestamp(now) }, cancellationToken);
		}
	}

	public class ImportSourcesRepository : ImportWorkspaceChildRepositoryBase<ImportSource>, IImportSourcesRepository
	{
		public ImportSourcesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => Col("CreatedOn");

		public Task<List<ImportSource>> GetWithExpiredCredentialsAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSource>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("EncryptedCredentials")} IS NOT NULL AND " +
				$"({Col("CredentialsExpireOn")} IS NULL OR {Col("CredentialsExpireOn")} < {P}Now) ORDER BY {Col("CreatedOn")}{Limit(take)}",
				new { Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> ClearCredentialsAsync(int departmentId, string sourceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("EncryptedCredentials")} = NULL, {Col("CredentialsExpireOn")} = NULL WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = sourceId }, cancellationToken);
		}
	}

	public class ImportArtifactsRepository : ImportWorkspaceChildRepositoryBase<ImportArtifact>, IImportArtifactsRepository
	{
		public ImportArtifactsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => Col("CreatedOn");

		public async Task<bool> TryAppendPartAsync(int departmentId, string artifactId, long expectedSizeBytes, long newSizeBytes,
			string uploadedPartsJson, DateTime now, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("SizeBytes")} = {P}NewSize, {Col("UploadedParts")} = {P}Parts, {Col("UpdatedOn")} = {P}Now " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")} AND {Col("SizeBytes")} = {P}ExpectedSize AND {Col("UploadState")} = {P}InProgress",
				new
				{
					DepartmentId = departmentId,
					Id = artifactId,
					NewSize = newSizeBytes,
					ExpectedSize = expectedSizeBytes,
					Parts = uploadedPartsJson,
					Now = DatabaseTimestamp(now),
					InProgress = (int)ImportArtifactUploadState.InProgress
				}, cancellationToken);

			return rows == 1;
		}

		public Task<List<ImportArtifact>> GetStaleUploadsAsync(DateTime before, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportArtifact>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("UploadState")} = {P}InProgress AND {Col("UpdatedOn")} < {P}Before ORDER BY {Col("UpdatedOn")}{Limit(take)}",
				new { InProgress = (int)ImportArtifactUploadState.InProgress, Before = DatabaseTimestamp(before) }, cancellationToken);
		}

		public Task<List<ImportArtifact>> GetPendingScansAsync(int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportArtifact>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("UploadState")} = {P}Completed AND {Col("ScanState")} = {P}Pending ORDER BY {Col("UpdatedOn")}{Limit(take)}",
				new { Completed = (int)ImportArtifactUploadState.Completed, Pending = (int)ImportArtifactScanState.Pending }, cancellationToken);
		}

		public Task<List<ImportArtifact>> GetPastRetentionAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportArtifact>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("PurgedOn")} IS NULL AND {Col("RetentionDeadline")} IS NOT NULL AND {Col("RetentionDeadline")} < {P}Now " +
				$"ORDER BY {Col("RetentionDeadline")}{Limit(take)}",
				new { Now = DatabaseTimestamp(now) }, cancellationToken);
		}
	}

	public class ImportDatasetsRepository : ImportWorkspaceChildRepositoryBase<ImportDataset>, IImportDatasetsRepository
	{
		public ImportDatasetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("Wave")}, {Col("CreatedOn")}, {Col("SheetIndex")}";

		public Task<List<ImportDataset>> GetForArtifactAsync(int departmentId, string artifactId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportDataset>(
				$"{Select} WHERE {ByDepartment} AND {Col("ArtifactId")} = {CaseInsensitive("ArtifactId")} ORDER BY {Col("SheetIndex")}",
				new { DepartmentId = departmentId, ArtifactId = artifactId }, cancellationToken);
		}

		public async Task<bool> UpdateWithRevisionAsync(ImportDataset dataset, int expectedRevision, CancellationToken cancellationToken = default)
		{
			var columns = new[]
			{
				"ArtifactId", "SourceId", "SheetName", "SheetIndex", "Name", "EntityKey", "ContractVersion", "DetectedProfileKey",
				"DetectionConfidence", "DestinationConfirmed", "ColumnMapId", "Status", "Profile", "ColumnCount", "RowCount", "ReadyCount",
				"WarningCount", "BlockedCount", "ExcludedCount", "CreatedCount", "UpdatedCount", "LinkedCount", "UnchangedCount",
				"SkippedCount", "FailedCount", "Wave", "StagingDigest", "ApprovedRunId", "SupersededById", "FailureCode", "StaffAuthored",
				"UpdatedOn", "AnalyzedOn"
			};

			var parameters = new DynamicParameters(dataset);
			parameters.Add("ExpectedRevision", expectedRevision);
			parameters.Add("NewRevision", expectedRevision + 1);
			parameters.Add("UpdatedOn", DatabaseTimestamp(dataset.UpdatedOn));
			parameters.Add("AnalyzedOn", dataset.AnalyzedOn.HasValue ? DatabaseTimestamp(dataset.AnalyzedOn.Value) : (DateTime?)null);

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {string.Join(", ", columns.Select(c => $"{Col(c)} = {P}{c}"))}, {Col("Revision")} = {P}NewRevision " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")} AND {Col("Revision")} = {P}ExpectedRevision",
				parameters, cancellationToken);

			if (rows == 1)
				dataset.Revision = expectedRevision + 1;

			return rows == 1;
		}
	}

	public class ImportColumnMapsRepository : ImportRepositoryBase<ImportColumnMap>, IImportColumnMapsRepository
	{
		public ImportColumnMapsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportColumnMap>> GetSavedForDepartmentAsync(int departmentId, string entityKey, CancellationToken cancellationToken = default)
		{
			var entity = string.IsNullOrWhiteSpace(entityKey) ? string.Empty : $" AND {Col("EntityKey")} = {CaseInsensitive("EntityKey")}";
			return ListAsync<ImportColumnMap>(
				$"{Select} WHERE {ByDepartment} AND {Col("IsSaved")} = {P}Saved{entity} ORDER BY {Col("Name")}, {Col("Version")} DESC",
				new { DepartmentId = departmentId, Saved = true, EntityKey = entityKey }, cancellationToken);
		}

		public Task<List<ImportColumnMap>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportColumnMap>(
				$"{Select} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")} ORDER BY {Col("CreatedOn")}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<int> DeleteUnsavedForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")} AND {Col("IsSaved")} = {P}Saved",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, Saved = false }, cancellationToken);
		}
	}

	public class ImportValueMapsRepository : ImportWorkspaceChildRepositoryBase<ImportValueMap>, IImportValueMapsRepository
	{
		public ImportValueMapsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportValueMap>> GetForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportValueMap>(
				$"{Select} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} ORDER BY {Col("FieldKey")}, {Col("RowCount")} DESC",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}

		public Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")}",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}
	}

	public class ImportStagedRowsRepository : ImportWorkspaceChildRepositoryBase<ImportStagedRow>, IImportStagedRowsRepository
	{
		public ImportStagedRowsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("DatasetId")}, {Col("RowNumber")}";

		public Task<List<ImportStagedRow>> GetPageAsync(int departmentId, string datasetId, int afterRowNumber, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportStagedRow>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} AND {Col("RowNumber")} > {P}After " +
				$"ORDER BY {Col("RowNumber")}{Limit(take)}",
				new { DepartmentId = departmentId, DatasetId = datasetId, After = afterRowNumber }, cancellationToken);
		}

		public async Task<List<ImportStagedRow>> GetByIdsAsync(int departmentId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
		{
			if (ids == null || ids.Count == 0)
				return new List<ImportStagedRow>();

			return await ListAsync<ImportStagedRow>(
				$"{Select} WHERE {ByDepartment} AND {CaseInsensitiveInList("Id", "Ids")} ORDER BY {Col("RowNumber")}",
				new { DepartmentId = departmentId, Ids = ids.ToArray() }, cancellationToken);
		}

		public Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")}",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}

		public Task UpdateStateAsync(ImportStagedRow row, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("SourceKey")} = {P}SourceKey, {Col("SourcePayload")} = {P}SourcePayload, {Col("CanonicalPayload")} = {P}CanonicalPayload, " +
				$"{Col("IsEncrypted")} = {P}IsEncrypted, {Col("ContentHash")} = {P}ContentHash, {Col("ValidationState")} = {P}ValidationState, " +
				$"{Col("Decision")} = {P}Decision, {Col("Outcome")} = {P}Outcome, {Col("TargetKey")} = {P}TargetKey, {Col("DependencyKeys")} = {P}DependencyKeys, " +
				$"{Col("TargetVersionAtPreview")} = {P}TargetVersionAtPreview, {Col("Excluded")} = {P}Excluded, {Col("EditedByStaff")} = {P}EditedByStaff, " +
				$"{Col("UpdatedOn")} = {P}UpdatedOn WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new
				{
					row.DepartmentId,
					row.Id,
					row.SourceKey,
					row.SourcePayload,
					row.CanonicalPayload,
					row.IsEncrypted,
					row.ContentHash,
					row.ValidationState,
					row.Decision,
					row.Outcome,
					row.TargetKey,
					row.DependencyKeys,
					row.TargetVersionAtPreview,
					row.Excluded,
					row.EditedByStaff,
					UpdatedOn = DatabaseTimestamp(row.UpdatedOn)
				}, cancellationToken);
		}

		public Task<int> SetPlanAsync(int departmentId, string rowId, int decision, string targetKey, string targetVersion, int validationState, DateTime now,
			CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Decision")} = {P}Decision, {Col("TargetKey")} = {P}TargetKey, {Col("TargetVersionAtPreview")} = {P}TargetVersion, " +
				$"{Col("ValidationState")} = {P}ValidationState, {Col("UpdatedOn")} = {P}Now WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = rowId, Decision = decision, TargetKey = targetKey, TargetVersion = targetVersion, ValidationState = validationState,
					Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> ResetOutcomesAsync(int departmentId, string datasetId, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Outcome")} = {P}None, {Col("UpdatedOn")} = {P}Now " +
				$"WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} AND {Col("Outcome")} <> {P}None",
				new { DepartmentId = departmentId, DatasetId = datasetId, None = (int)ImportRowOutcome.None, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> SetOutcomeAsync(int departmentId, string rowId, int outcome, string targetKey, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Outcome")} = {P}Outcome, {Col("TargetKey")} = {P}TargetKey, {Col("UpdatedOn")} = {P}Now " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = rowId, Outcome = outcome, TargetKey = targetKey, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<List<ImportStagedRow>> GetDigestInputsAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportStagedRow>(
				$"SELECT {Cols("Id", "RowNumber", "SourceKey", "ContentHash", "ValidationState", "Decision", "Outcome", "TargetKey", "Excluded", "IsExample")} FROM {Tbl(Table)} " +
				$"WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} ORDER BY {Col("RowNumber")}",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}
	}

	public class ImportIssuesRepository : ImportWorkspaceChildRepositoryBase<ImportIssue>, IImportIssuesRepository
	{
		public ImportIssuesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("Code")}, {Col("RowNumber")}";

		public Task<List<ImportIssue>> GetForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportIssue>(
				$"{Select} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} ORDER BY {Col("Code")}, {Col("RowNumber")}",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}

		public Task<List<ImportIssue>> GetPageAsync(int departmentId, string workspaceId, string datasetId, int? severity, int? status, string code,
			int skip, int take, CancellationToken cancellationToken = default)
		{
			var where = new StringBuilder(ByWorkspace);
			if (!string.IsNullOrWhiteSpace(datasetId)) where.Append($" AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")}");
			if (severity.HasValue) where.Append($" AND {Col("Severity")} = {P}Severity");
			if (status.HasValue) where.Append($" AND {Col("Status")} = {P}Status");
			if (!string.IsNullOrWhiteSpace(code)) where.Append($" AND {Col("Code")} = {CaseInsensitive("Code")}");

			return ListAsync<ImportIssue>(
				$"{Select} WHERE {where} ORDER BY {Col("Severity")} DESC, {Col("Code")}, {Col("RowNumber")}, {Col("Id")}{Page(skip, take)}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, DatasetId = datasetId, Severity = severity ?? 0, Status = status ?? 0, Code = code },
				cancellationToken);
		}

		public Task<int> CountOpenBlockingAsync(int departmentId, string workspaceId, string datasetId, CancellationToken cancellationToken = default)
		{
			var dataset = string.IsNullOrWhiteSpace(datasetId) ? string.Empty : $" AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")}";
			return ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {ByWorkspace}{dataset} AND {Col("Status")} = {P}Open AND {Col("Severity")} = {P}Blocking",
				new
				{
					DepartmentId = departmentId,
					WorkspaceId = workspaceId,
					DatasetId = datasetId,
					Open = (int)ImportIssueStatus.Open,
					Blocking = (int)ImportIssueSeverity.Blocking
				}, cancellationToken);
		}

		public Task<int> DeleteOpenSystemIssuesAsync(int departmentId, string datasetId, int fromLevel, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")} AND {Col("Level")} >= {P}FromLevel " +
				$"AND {Col("Status")} = {P}Open",
				new { DepartmentId = departmentId, DatasetId = datasetId, FromLevel = fromLevel, Open = (int)ImportIssueStatus.Open }, cancellationToken);
		}

		public Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("DatasetId")} = {CaseInsensitive("DatasetId")}",
				new { DepartmentId = departmentId, DatasetId = datasetId }, cancellationToken);
		}
	}

	public class ImportExternalIdsRepository : ImportRepositoryBase<ImportExternalId>, IImportExternalIdsRepository
	{
		public ImportExternalIdsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string ByIdentity => $"{ByDepartment} AND {Col("SourceSystemKey")} = {CaseInsensitive("SourceSystemKey")} AND {Col("EntityType")} = {CaseInsensitive("EntityType")}";

		public Task<ImportExternalId> GetByIdentityAsync(int departmentId, string sourceSystemKey, string entityType, string sourceId, CancellationToken cancellationToken = default)
		{
			return QueryFirstOrDefaultAsync<ImportExternalId>(
				$"{Select} WHERE {ByIdentity} AND {Col("SourceId")} = {CaseInsensitive("SourceId")}",
				new { DepartmentId = departmentId, SourceSystemKey = sourceSystemKey, EntityType = entityType, SourceId = sourceId }, cancellationToken);
		}

		public async Task<List<ImportExternalId>> GetForSourceIdsAsync(int departmentId, string sourceSystemKey, string entityType, IReadOnlyCollection<string> sourceIds,
			CancellationToken cancellationToken = default)
		{
			var result = new List<ImportExternalId>();
			if (sourceIds == null || sourceIds.Count == 0)
				return result;

			// Batches keep the IN list well under the SQL Server parameter limit.
			foreach (var batch in sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Select((id, i) => new { id, i }).GroupBy(x => x.i / 1000))
			{
				result.AddRange(await ListAsync<ImportExternalId>(
					$"{Select} WHERE {ByIdentity} AND {CaseInsensitiveInList("SourceId", "SourceIds")}",
					new { DepartmentId = departmentId, SourceSystemKey = sourceSystemKey, EntityType = entityType, SourceIds = batch.Select(x => x.id).ToArray() },
					cancellationToken));
			}

			return result;
		}

		public async Task<bool> TryClaimAsync(ImportExternalId claim, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(claim.Id))
				claim.Id = Guid.NewGuid().ToString();

			var columns = new[] { "Id", "DepartmentId", "WorkspaceId", "SourceSystemKey", "EntityType", "SourceId", "TargetKey", "State", "RunId", "CreatedOn", "UpdatedOn" };
			var values = string.Join(", ", columns.Select(c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(columns)}) VALUES ({values}) ON CONFLICT ({Cols("DepartmentId", "SourceSystemKey", "EntityType", "SourceId")}) DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(columns)}) SELECT {values} WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) " +
				  $"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceSystemKey")} = {P}SourceSystemKey AND {Col("EntityType")} = {P}EntityType AND {Col("SourceId")} = {P}SourceId)";

			try
			{
				return await ExecuteAsync(sql, new
				{
					claim.Id,
					claim.DepartmentId,
					claim.WorkspaceId,
					claim.SourceSystemKey,
					claim.EntityType,
					claim.SourceId,
					claim.TargetKey,
					claim.State,
					claim.RunId,
					CreatedOn = DatabaseTimestamp(claim.CreatedOn),
					UpdatedOn = DatabaseTimestamp(claim.UpdatedOn)
				}, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public Task<int> SetStateAsync(int departmentId, string id, ImportExternalIdState state, string targetKey, string runId, DateTime now,
			CancellationToken cancellationToken = default)
		{
			// Built conditionally rather than COALESCE(@p, col): a null parameter keeps the stored value.
			var set = new StringBuilder($"{Col("State")} = {P}State, {Col("UpdatedOn")} = {P}Now");
			if (targetKey != null) set.Append($", {Col("TargetKey")} = {P}TargetKey");
			if (runId != null) set.Append($", {Col("RunId")} = {P}RunId");

			return ExecuteAsync($"UPDATE {Tbl(Table)} SET {set} WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = id, State = (int)state, TargetKey = targetKey, RunId = runId, Now = DatabaseTimestamp(now) },
				cancellationToken);
		}

		public Task<List<ImportExternalId>> GetClaimedForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportExternalId>(
				$"{Select} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")} AND {Col("State")} = {P}Claimed",
				new { DepartmentId = departmentId, RunId = runId, Claimed = (int)ImportExternalIdState.Claimed }, cancellationToken);
		}

		public Task<List<ImportExternalId>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportExternalId>(
				$"{Select} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")} ORDER BY {Col("CreatedOn")}",
				new { DepartmentId = departmentId, RunId = runId }, cancellationToken);
		}

		public Task<int> DeleteForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}
	}

	public class ImportRunsRepository : ImportWorkspaceChildRepositoryBase<ImportRun>, IImportRunsRepository
	{
		public ImportRunsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("RequestedOn")} DESC";

		/// <summary>The runnable predicate shared by both claim paths (section 10.4).</summary>
		private string Runnable =>
			$"(({Col("Status")} = {P}Queued AND ({Col("NextAttemptOn")} IS NULL OR {Col("NextAttemptOn")} <= {P}Now)) " +
			$"OR ({Col("Status")} = {P}Failed AND {Col("NextAttemptOn")} IS NOT NULL AND {Col("NextAttemptOn")} <= {P}Now AND {Col("Attempts")} < {P}MaxAttempts) " +
			$"OR ({InList("Status", "Leased")} AND {Col("LeaseExpiresOn")} IS NOT NULL AND {Col("LeaseExpiresOn")} < {P}Now)) " +
			$"AND {Col("CancelRequested")} = {P}NoCancel";

		private object ClaimParameters(string leaseOwner, DateTime leaseUntil, DateTime now, int maxAttempts, string runId = null) => new
		{
			Owner = Truncate(leaseOwner, 256),
			Until = DatabaseTimestamp(leaseUntil),
			Now = DatabaseTimestamp(now),
			MaxAttempts = maxAttempts,
			Queued = (int)ImportRunStatus.Queued,
			Failed = (int)ImportRunStatus.Failed,
			Running = (int)ImportRunStatus.Running,
			Leased = InListValue(new[] { (int)ImportRunStatus.Running, (int)ImportRunStatus.Reconciling }),
			NoCancel = (int)ImportRunCancelRequest.None,
			Id = runId
		};

		// A reconciling run keeps its status when re-leased; everything else becomes Running.
		private string ClaimSet =>
			$"{Col("Status")} = CASE WHEN {Col("Status")} = {(int)ImportRunStatus.Reconciling} THEN {Col("Status")} ELSE {P}Running END, " +
			$"{Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("HeartbeatOn")} = {P}Now, " +
			$"{Col("StartedOn")} = COALESCE({Col("StartedOn")}, {P}Now), {Col("CursorMovedOn")} = COALESCE({Col("CursorMovedOn")}, {P}Now), {Col("UpdatedOn")} = {P}Now";

		public async Task<ImportRun> ClaimNextAsync(string leaseOwner, DateTime leaseUntil, DateTime now, int maxAttempts, CancellationToken cancellationToken = default)
		{
			var sql = IsPostgres
				? $"UPDATE {Tbl(Table)} SET {ClaimSet} WHERE {Col("Id")} IN (SELECT {Col("Id")} FROM {Tbl(Table)} WHERE {Runnable} " +
				  $"ORDER BY {Col("UpdatedOn")}, {Col("RequestedOn")} LIMIT 1 FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"WITH next AS (SELECT TOP (1) * FROM {Tbl(Table)} WITH (READPAST, UPDLOCK, ROWLOCK) WHERE {Runnable} " +
				  $"ORDER BY {Col("UpdatedOn")}, {Col("RequestedOn")}) UPDATE next SET {ClaimSet} OUTPUT inserted.*";

			return (await QueryAsync<ImportRun>(sql, ClaimParameters(leaseOwner, leaseUntil, now, maxAttempts), cancellationToken))?.FirstOrDefault();
		}

		public async Task<ImportRun> ClaimByIdAsync(string runId, string leaseOwner, DateTime leaseUntil, DateTime now, int maxAttempts, CancellationToken cancellationToken = default)
		{
			var sql = IsPostgres
				? $"UPDATE {Tbl(Table)} SET {ClaimSet} WHERE {Col("Id")} IN (SELECT {Col("Id")} FROM {Tbl(Table)} WHERE {Col("Id")} = {CaseInsensitive("Id")} AND {Runnable} " +
				  $"FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"UPDATE {Tbl(Table)} WITH (READPAST, ROWLOCK) SET {ClaimSet} OUTPUT inserted.* WHERE {Col("Id")} = {P}Id AND {Runnable}";

			return (await QueryAsync<ImportRun>(sql, ClaimParameters(leaseOwner, leaseUntil, now, maxAttempts, runId), cancellationToken))?.FirstOrDefault();
		}

		public async Task<bool> HeartbeatAsync(string runId, string leaseOwner, DateTime leaseUntil, DateTime now, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("LeaseExpiresOn")} = {P}Until, {Col("HeartbeatOn")} = {P}Now " +
				$"WHERE {Col("Id")} = {CaseInsensitive("Id")} AND {Col("LeaseOwner")} = {P}Owner",
				new { Id = runId, Owner = Truncate(leaseOwner, 256), Until = DatabaseTimestamp(leaseUntil), Now = DatabaseTimestamp(now) }, cancellationToken);

			return rows == 1;
		}

		public async Task<bool> SaveLeasedAsync(ImportRun run, string leaseOwner, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Status, {Col("LeaseOwner")} = {P}LeaseOwner, {Col("LeaseExpiresOn")} = {P}LeaseExpiresOn, " +
				$"{Col("HeartbeatOn")} = {P}HeartbeatOn, {Col("Attempts")} = {P}Attempts, {Col("NextAttemptOn")} = {P}NextAttemptOn, {Col("Cursor")} = {P}Cursor, " +
				$"{Col("CursorMovedOn")} = {P}CursorMovedOn, {Col("Counts")} = {P}Counts, {Col("LastErrorCode")} = {P}LastErrorCode, " +
				$"{Col("DepartmentLockId")} = {P}DepartmentLockId, {Col("StartedOn")} = {P}StartedOn, {Col("CompletedOn")} = {P}CompletedOn, " +
				$"{Col("UpdatedOn")} = {P}UpdatedOn WHERE {Col("Id")} = {CaseInsensitive("Id")} AND {Col("LeaseOwner")} = {P}Owner",
				new
				{
					run.Id,
					Owner = Truncate(leaseOwner, 256),
					run.Status,
					LeaseOwner = Truncate(run.LeaseOwner, 256),
					LeaseExpiresOn = Timestamp(run.LeaseExpiresOn),
					HeartbeatOn = Timestamp(run.HeartbeatOn),
					run.Attempts,
					NextAttemptOn = Timestamp(run.NextAttemptOn),
					run.Cursor,
					CursorMovedOn = Timestamp(run.CursorMovedOn),
					run.Counts,
					run.LastErrorCode,
					run.DepartmentLockId,
					StartedOn = Timestamp(run.StartedOn),
					CompletedOn = Timestamp(run.CompletedOn),
					UpdatedOn = DatabaseTimestamp(run.UpdatedOn)
				}, cancellationToken);

			return rows == 1;
		}

		public Task<int> RequestCancelAsync(int departmentId, string runId, ImportRunCancelRequest request, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("CancelRequested")} = {P}Request, {Col("UpdatedOn")} = {P}Now WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = runId, Request = (int)request, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<List<ImportRun>> GetOpenForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportRun>(
				$"{Select} WHERE {ByDepartment} AND {InList("Status", "Open")} ORDER BY {Col("RequestedOn")}",
				new
				{
					DepartmentId = departmentId,
					Open = InListValue(new[]
					{
						(int)ImportRunStatus.Queued, (int)ImportRunStatus.Running, (int)ImportRunStatus.Paused,
						(int)ImportRunStatus.Reconciling, (int)ImportRunStatus.AwaitingChecker
					})
				}, cancellationToken);
		}

		public Task<List<ImportRun>> GetStalledAsync(DateTime cursorBefore, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportRun>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {InList("Status", "Leased")} AND {Col("CursorMovedOn")} IS NOT NULL AND {Col("CursorMovedOn")} < {P}Before " +
				$"ORDER BY {Col("CursorMovedOn")}{Limit(take)}",
				new { Leased = InListValue(new[] { (int)ImportRunStatus.Running, (int)ImportRunStatus.Reconciling }), Before = DatabaseTimestamp(cursorBefore) },
				cancellationToken);
		}

		public Task<List<ImportRun>> GetRecentAsync(DateTime since, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportRun>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("UpdatedOn")} >= {P}Since ORDER BY {Col("UpdatedOn")} DESC{Limit(take)}",
				new { Since = DatabaseTimestamp(since) }, cancellationToken);
		}

		private static DateTime? Timestamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;
	}

	public class ImportChangeJournalRepository : ImportRepositoryBase<ImportChangeJournalEntry>, IImportChangeJournalRepository
	{
		public ImportChangeJournalRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportChangeJournalEntry>> GetForRunDescendingAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportChangeJournalEntry>(
				$"{Select} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")} ORDER BY {Col("Sequence")} DESC",
				new { DepartmentId = departmentId, RunId = runId }, cancellationToken);
		}

		public async Task<long> GetMaxSequenceAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return await ScalarAsync<long?>(
				$"SELECT MAX({Col("Sequence")}) FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")}",
				new { DepartmentId = departmentId, RunId = runId }, cancellationToken) ?? 0;
		}

		public Task<int> MarkRolledBackAsync(int departmentId, string id, ImportRollbackOutcome outcome, string rollbackRunId, DateTime now,
			CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RollbackOutcome")} = {P}Outcome, {Col("RollbackRunId")} = {P}RollbackRunId, {Col("RolledBackOn")} = {P}Now " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")}",
				new { DepartmentId = departmentId, Id = id, Outcome = (int)outcome, RollbackRunId = rollbackRunId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> DeleteForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}
	}

	public class ImportReconciliationsRepository : ImportRepositoryBase<ImportReconciliation>, IImportReconciliationsRepository
	{
		public ImportReconciliationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportReconciliation>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportReconciliation>(
				$"{Select} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")} ORDER BY {Col("CheckId")}",
				new { DepartmentId = departmentId, RunId = runId }, cancellationToken);
		}

		public Task<int> DeleteForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {ByDepartment} AND {Col("RunId")} = {CaseInsensitive("RunId")}",
				new { DepartmentId = departmentId, RunId = runId }, cancellationToken);
		}
	}

	public class ImportSupportGrantsRepository : ImportRepositoryBase<ImportSupportGrant>, IImportSupportGrantsRepository
	{
		public ImportSupportGrantsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportSupportGrant>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSupportGrant>(
				$"{Select} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")} ORDER BY {Col("GrantedOn")} DESC",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<List<ImportSupportGrant>> GetExpiredActiveAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSupportGrant>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("Status")} = {P}Active AND {Col("ExpiresOn")} < {P}Now ORDER BY {Col("ExpiresOn")}{Limit(take)}",
				new { Active = (int)ImportGrantStatus.Active, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<List<ImportSupportGrant>> GetPendingApprovalAsync(int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSupportGrant>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("Status")} = {P}Pending ORDER BY {Col("GrantedOn")}{Limit(take)}",
				new { Pending = (int)ImportGrantStatus.PendingApproval }, cancellationToken);
		}
	}

	public class ImportSupportRequestsRepository : ImportRepositoryBase<ImportSupportRequest>, IImportSupportRequestsRepository
	{
		public ImportSupportRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<List<ImportSupportRequest>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSupportRequest>(
				$"{Select} WHERE {ByDepartment} AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")} ORDER BY {Col("OpenedOn")} DESC",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<List<ImportSupportRequest>> GetQueueAsync(bool openOnly, int skip, int take, CancellationToken cancellationToken = default)
		{
			var where = openOnly ? $" WHERE {InList("Status", "Open")}" : string.Empty;
			return ListAsync<ImportSupportRequest>(
				$"{Select}{where} ORDER BY {Col("Priority")} DESC, {Col("OpenedOn")}, {Col("Id")}{Page(skip, take)}",
				new
				{
					Open = InListValue(new[]
					{
						(int)ImportSupportRequestStatus.Open, (int)ImportSupportRequestStatus.Acknowledged, (int)ImportSupportRequestStatus.WaitingOnDepartment
					})
				}, cancellationToken);
		}

		public Task<List<ImportSupportRequest>> GetHelpdeskDueAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSupportRequest>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("HelpdeskState")} = {P}Pending AND ({Col("HelpdeskNextAttemptOn")} IS NULL OR {Col("HelpdeskNextAttemptOn")} <= {P}Now) " +
				$"ORDER BY {Col("OpenedOn")}{Limit(take)}",
				new { Pending = (int)ImportHelpdeskState.Pending, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public async Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken = default)
		{
			return await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {Col("Number")} = {CaseInsensitive("Number")}",
				new { Number = number }, cancellationToken) > 0;
		}
	}

	public class ImportMessagesRepository : ImportWorkspaceChildRepositoryBase<ImportMessage>, IImportMessagesRepository
	{
		public ImportMessagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => Col("CreatedOn");

		public Task<List<ImportMessage>> GetPageAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportMessage>(
				$"{Select} WHERE {ByWorkspace} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")}{Page(skip, take)}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<int> CountUnreadByDepartmentAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {ByWorkspace} AND {Col("ReadByDepartmentOn")} IS NULL AND {Col("AuthorKind")} <> {P}Administrator",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, Administrator = (int)ImportActorKind.Administrator }, cancellationToken);
		}

		public Task<int> MarkReadByDepartmentAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ReadByDepartmentOn")} = {P}Now WHERE {ByWorkspace} AND {Col("ReadByDepartmentOn")} IS NULL",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<List<ImportMessage>> GetHelpdeskDueAsync(DateTime now, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportMessage>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("HelpdeskState")} = {P}Pending AND ({Col("HelpdeskNextAttemptOn")} IS NULL OR {Col("HelpdeskNextAttemptOn")} <= {P}Now) " +
				$"ORDER BY {Col("CreatedOn")}{Limit(take)}",
				new { Pending = (int)ImportHelpdeskState.Pending, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> MarkReadByStaffAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ReadByStaffOn")} = {P}Now WHERE {ByWorkspace} AND {Col("ReadByStaffOn")} IS NULL",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}
	}

	public class ImportSuggestionsRepository : ImportWorkspaceChildRepositoryBase<ImportSuggestion>, IImportSuggestionsRepository
	{
		public ImportSuggestionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("SuggestedOn")} DESC";

		public Task<List<ImportSuggestion>> GetProposedAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportSuggestion>(
				$"{Select} WHERE {ByWorkspace} AND {Col("Status")} = {P}Proposed ORDER BY {Col("SuggestedOn")}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId, Proposed = (int)ImportSuggestionStatus.Proposed }, cancellationToken);
		}
	}

	public class ImportConfigurationChangesRepository : ImportWorkspaceChildRepositoryBase<ImportConfigurationChange>, IImportConfigurationChangesRepository
	{
		public ImportConfigurationChangesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => Col("ItemKey");

		public async Task<bool> UpdateWithRevisionAsync(ImportConfigurationChange change, int expectedRevision, CancellationToken cancellationToken = default)
		{
			var columns = new[]
			{
				"Origin", "OriginKey", "Value", "State", "ApprovedRunId", "AppliedRunId", "FailureCode", "StaffAuthored", "UpdatedBy", "UpdatedOn", "AppliedOn"
			};

			var parameters = new DynamicParameters(change);
			parameters.Add("ExpectedRevision", expectedRevision);
			parameters.Add("NewRevision", expectedRevision + 1);
			parameters.Add("UpdatedOn", DatabaseTimestamp(change.UpdatedOn));
			parameters.Add("AppliedOn", change.AppliedOn.HasValue ? DatabaseTimestamp(change.AppliedOn.Value) : (DateTime?)null);

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {string.Join(", ", columns.Select(c => $"{Col(c)} = {P}{c}"))}, {Col("Revision")} = {P}NewRevision " +
				$"WHERE {ByDepartment} AND {Col("Id")} = {CaseInsensitive("Id")} AND {Col("Revision")} = {P}ExpectedRevision",
				parameters, cancellationToken);

			if (rows == 1)
				change.Revision = expectedRevision + 1;

			return rows == 1;
		}

		public Task<List<ImportConfigurationChange>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportConfigurationChange>(
				$"{Select} WHERE {ByDepartment} AND {Col("ApprovedRunId")} = {CaseInsensitive("ApprovedRunId")} ORDER BY {Col("ItemKey")}",
				new { DepartmentId = departmentId, ApprovedRunId = runId }, cancellationToken);
		}
	}

	public class ImportEventsRepository : ImportWorkspaceChildRepositoryBase<ImportEvent>, IImportEventsRepository
	{
		public ImportEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		protected override string WorkspaceOrder => $"{Col("CreatedOn")} DESC";

		public Task<List<ImportEvent>> GetPageAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportEvent>(
				$"{Select} WHERE {ByWorkspace} ORDER BY {Col("CreatedOn")} DESC, {Col("Id")}{Page(skip, take)}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken);
		}

		public Task<List<ImportEvent>> GetByActionSinceAsync(string action, DateTime since, int take, CancellationToken cancellationToken = default)
		{
			return ListAsync<ImportEvent>(
				$"{Top(take)} FROM {Tbl(Table)} WHERE {Col("Action")} = {CaseInsensitive("Action")} AND {Col("CreatedOn")} >= {P}Since " +
				$"ORDER BY {Col("CreatedOn")}{Limit(take)}",
				new { Action = action, Since = DatabaseTimestamp(since) }, cancellationToken);
		}
	}
	public class ImportInvitesRepository : RmsRepositoryBase<Invite>, IImportInvitesRepository
	{
		public ImportInvitesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string ImportKinds => $"{Col("Kind")} IN ({(int)InviteKinds.ImportAccount}, {(int)InviteKinds.ImportJoin})";

		public async Task<List<Invite>> GetDueAsync(DateTime now, int maxAttempts, int take, CancellationToken cancellationToken = default)
		{
			var top = IsPostgres ? "SELECT *" : $"SELECT TOP ({Math.Max(1, take)}) *";
			var limit = IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;
			return (await QueryAsync<Invite>(
				$"{top} FROM {Tbl("Invites")} WHERE {ImportKinds} AND {Col("SendState")} = {P}Pending AND {Col("CompletedOn")} IS NULL " +
				$"AND ({Col("SendAfter")} IS NULL OR {Col("SendAfter")} <= {P}Now) AND {Col("SendAttempts")} < {P}MaxAttempts " +
				$"ORDER BY {Col("SendAfter")}, {Col("InviteId")}{limit}",
				new { Pending = (int)InviteSendStates.Pending, Now = DatabaseTimestamp(now), MaxAttempts = maxAttempts }, cancellationToken)).ToList();
		}

		public async Task<List<Invite>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			return (await QueryAsync<Invite>(
				$"SELECT * FROM {Tbl("Invites")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("WorkspaceId")} = {CaseInsensitive("WorkspaceId")} " +
				$"ORDER BY {Col("InviteId")}",
				new { DepartmentId = departmentId, WorkspaceId = workspaceId }, cancellationToken)).ToList();
		}

		public Task<Invite> GetByCodeAsync(Guid code, CancellationToken cancellationToken = default)
		{
			return QueryFirstOrDefaultAsync<Invite>($"SELECT * FROM {Tbl("Invites")} WHERE {Col("Code")} = {P}Code", new { Code = code }, cancellationToken);
		}

		public Task<Invite> GetByDepartmentAndCodeAsync(int departmentId, Guid code, CancellationToken cancellationToken = default)
		{
			return QueryFirstOrDefaultAsync<Invite>(
				$"SELECT * FROM {Tbl("Invites")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Code")} = {P}Code",
				new { DepartmentId = departmentId, Code = code }, cancellationToken);
		}

		public Task<int> SetSendStateAsync(int inviteId, InviteSendStates state, int attempts, DateTime? sentOn, DateTime? nextAttempt, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("Invites")} SET {Col("SendState")} = {P}State, {Col("SendAttempts")} = {P}Attempts, " +
				$"{Col("SentOn")} = COALESCE({P}SentOn, {Col("SentOn")}), {Col("SendAfter")} = COALESCE({P}NextAttempt, {Col("SendAfter")}) " +
				$"WHERE {Col("InviteId")} = {P}InviteId",
				new
				{
					InviteId = inviteId,
					State = (int)state,
					Attempts = attempts,
					SentOn = sentOn.HasValue ? DatabaseTimestamp(sentOn.Value) : (DateTime?)null,
					NextAttempt = nextAttempt.HasValue ? DatabaseTimestamp(nextAttempt.Value) : (DateTime?)null
				}, cancellationToken);
		}
	}
}
