using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Protocol version translations (P3, M0380). Header reads never select ContentJson or ContentHtml.</summary>
	public class ProtocolVersionTranslationsRepository : RmsRepositoryBase<ProtocolVersionTranslation>, IProtocolVersionTranslationsRepository
	{
		private static readonly string[] HeaderColumns =
		{
			"ProtocolVersionTranslationId", "DepartmentId", "DispatchProtocolId", "ProtocolVersionId", "Locale", "Status", "ContentHash", "SourceContentHash",
			"CreatedOn", "CreatedByUserId", "UpdatedOn", "UpdatedByUserId", "ApprovedOn", "ApprovedByUserId", "SignatureRecordId", "WithdrawnOn",
			"WithdrawnByUserId", "RowVersion"
		};

		public ProtocolVersionTranslationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolVersionTranslation>> GetForVersionAsync(int departmentId, int protocolVersionId, bool withContent = false)
		{
			return (await QueryAsync<ProtocolVersionTranslation>(
				$"SELECT {(withContent ? "*" : Cols(HeaderColumns))} FROM {Tbl("ProtocolVersionTranslations")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("ProtocolVersionId")} = {P}VersionId ORDER BY {Col("Locale")}",
				new { DepartmentId = departmentId, VersionId = protocolVersionId })).ToList();
		}

		public async Task<List<ProtocolVersionTranslation>> GetApprovedHeadersForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds)
		{
			var ids = InListValue((protocolVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<ProtocolVersionTranslation>();

			return (await QueryAsync<ProtocolVersionTranslation>(
				$"SELECT {Cols(HeaderColumns)} FROM {Tbl("ProtocolVersionTranslations")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("Status")} = {(int)ProtocolTranslationStatuses.Approved} AND {InList("ProtocolVersionId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<ProtocolVersionTranslation> GetByIdForDepartmentAsync(int departmentId, int protocolVersionTranslationId)
		{
			return QueryFirstOrDefaultAsync<ProtocolVersionTranslation>(
				$"SELECT * FROM {Tbl("ProtocolVersionTranslations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionTranslationId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolVersionTranslationId });
		}

		public async Task<bool> TryUpdateAsync(ProtocolVersionTranslation translation, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (translation == null || translation.ProtocolVersionTranslationId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("Id", translation.ProtocolVersionTranslationId);
			parameters.Add("DepartmentId", translation.DepartmentId);
			parameters.Add("Expected", expectedRowVersion);
			parameters.Add("Next", expectedRowVersion + 1);
			parameters.Add("Status", translation.Status);
			parameters.Add("ContentJson", translation.ContentJson, System.Data.DbType.String);
			parameters.Add("ContentHtml", translation.ContentHtml, System.Data.DbType.String);
			parameters.Add("ContentHash", translation.ContentHash, System.Data.DbType.String);
			parameters.Add("SourceContentHash", translation.SourceContentHash, System.Data.DbType.String);
			parameters.Add("UpdatedOn", Stamp(translation.UpdatedOn), System.Data.DbType.DateTime2);
			parameters.Add("UpdatedByUserId", translation.UpdatedByUserId, System.Data.DbType.String);
			parameters.Add("ApprovedOn", Stamp(translation.ApprovedOn), System.Data.DbType.DateTime2);
			parameters.Add("ApprovedByUserId", translation.ApprovedByUserId, System.Data.DbType.String);
			parameters.Add("SignatureRecordId", translation.SignatureRecordId, System.Data.DbType.Int32);
			parameters.Add("WithdrawnOn", Stamp(translation.WithdrawnOn), System.Data.DbType.DateTime2);
			parameters.Add("WithdrawnByUserId", translation.WithdrawnByUserId, System.Data.DbType.String);

			var set = string.Join(", ", new[]
			{
				"Status", "ContentJson", "ContentHtml", "ContentHash", "SourceContentHash", "UpdatedOn", "UpdatedByUserId", "ApprovedOn", "ApprovedByUserId",
				"SignatureRecordId", "WithdrawnOn", "WithdrawnByUserId"
			}.Select(c => $"{Col(c)} = {P}{c}"));

			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolVersionTranslations")} SET {set}, {Col("RowVersion")} = {P}Next " +
				$"WHERE {Col("ProtocolVersionTranslationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}Expected",
				parameters, cancellationToken);

			if (changed == 1)
				translation.RowVersion = expectedRowVersion + 1;

			return changed == 1;
		}

		private static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;
	}
}
