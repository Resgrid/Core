using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Department export templates (registry M0177). Every query begins at DepartmentId; the schedule sweep is the one cross-department read.</summary>
	public class RmsExportTemplatesRepository : RmsRepositoryBase<RmsExportTemplate>, IRmsExportTemplatesRepository
	{
		public RmsExportTemplatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsExportTemplate> GetByIdForDepartmentAsync(int departmentId, string templateId)
		{
			return QueryFirstOrDefaultAsync<RmsExportTemplate>(
				$"SELECT * FROM {Tbl("RmsExportTemplates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExportTemplateId")} = {P}Id AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = templateId });
		}

		public Task<RmsExportTemplate> GetByKeyAsync(int departmentId, string templateKey)
		{
			return QueryFirstOrDefaultAsync<RmsExportTemplate>(
				$"SELECT * FROM {Tbl("RmsExportTemplates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TemplateKey")} = {P}Key AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Key = templateKey });
		}

		public Task<IEnumerable<RmsExportTemplate>> GetForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsExportTemplate>(
				$"SELECT * FROM {Tbl("RmsExportTemplates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Name")}",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsExportTemplate>> GetDueAsync(DateTime utcNow, int take)
		{
			var limit = Math.Clamp(take, 1, 500);
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("RmsExportTemplates")} WHERE {Col("IsEnabled")} = TRUE AND {Col("ScheduleKind")} <> 0 AND {Col("NextRunOn")} IS NOT NULL AND {Col("NextRunOn")} <= {P}Now AND {Col("DeletedOn")} IS NULL ORDER BY {Col("NextRunOn")} LIMIT {limit}"
				: $"SELECT TOP {limit} * FROM {Tbl("RmsExportTemplates")} WHERE {Col("IsEnabled")} = 1 AND {Col("ScheduleKind")} <> 0 AND {Col("NextRunOn")} IS NOT NULL AND {Col("NextRunOn")} <= {P}Now AND {Col("DeletedOn")} IS NULL ORDER BY {Col("NextRunOn")}";
			return QueryAsync<RmsExportTemplate>(sql, new { Now = utcNow });
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string templateId, long expectedVersion, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("RmsExportTemplates")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExportTemplateId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = templateId, Version = expectedVersion }, cancellationToken) == 1;
		}
	}

	/// <summary>Rendered export artifacts (registry M0177). The Data column is read only by the endpoints that serve the file.</summary>
	public class RmsExportRunsRepository : RmsRepositoryBase<RmsExportRun>, IRmsExportRunsRepository
	{
		private static readonly string MetadataColumns = Cols(
			"RmsExportRunId", "DepartmentId", "ProtectionId", "TemplateId", "TemplateKey", "Trigger", "RecordId", "WindowStart", "WindowEnd", "RecordCount",
			"FileName", "ContentType", "ByteSize", "Checksum", "Redacted", "RedactedFieldsJson", "GeneratedOn", "GeneratedByUserId", "WorkflowRunId", "ExpiresOn",
			"IsProtected", "ProtectedCatalogVersion", "DeletedOn");

		public RmsExportRunsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsExportRun> GetByIdForDepartmentAsync(int departmentId, string runId)
		{
			return QueryFirstOrDefaultAsync<RmsExportRun>(
				$"SELECT {MetadataColumns} FROM {Tbl("RmsExportRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExportRunId")} = {P}Id AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = runId });
		}

		public Task<RmsExportRun> GetWithDataAsync(int departmentId, string runId)
		{
			return QueryFirstOrDefaultAsync<RmsExportRun>(
				$"SELECT * FROM {Tbl("RmsExportRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExportRunId")} = {P}Id AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = runId });
		}

		public Task<IEnumerable<RmsExportRun>> GetForTemplateAsync(int departmentId, string templateId, int take)
		{
			var limit = Math.Clamp(take, 1, 500);
			var sql = IsPostgres
				? $"SELECT {MetadataColumns} FROM {Tbl("RmsExportRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TemplateId")} = {P}TemplateId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("GeneratedOn")} DESC LIMIT {limit}"
				: $"SELECT TOP {limit} {MetadataColumns} FROM {Tbl("RmsExportRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TemplateId")} = {P}TemplateId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("GeneratedOn")} DESC";
			return QueryAsync<RmsExportRun>(sql, new { DepartmentId = departmentId, TemplateId = templateId });
		}

		public Task<int> DeleteExpiredAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// The bytes go first and the row stays as a tombstone, so a run log that names the run still resolves.
			return ExecuteAsync(
				$"UPDATE {Tbl("RmsExportRuns")} SET {Col("Data")} = NULL, {Col("DeletedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ExpiresOn")} <= {P}Now AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Now = utcNow }, cancellationToken);
		}
	}
}
