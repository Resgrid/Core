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
	/// <summary>Dapper access to the DepartmentProfiles row (the dormant Connect-era table elevated to the branding source).</summary>
	public class DepartmentProfileRepository : RmsRepositoryBase<DepartmentProfile>, IDepartmentProfileRepository
	{
		public DepartmentProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DepartmentProfile> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<DepartmentProfile>(
				$"SELECT * FROM {Tbl("DepartmentProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}
	}

	public class DepartmentProfileMediaRepository : RmsRepositoryBase<DepartmentProfileMedia>, IDepartmentProfileMediaRepository
	{
		public DepartmentProfileMediaRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string MetadataColumns()
		{
			return string.Join(", ", new[]
			{
				Col("DepartmentProfileMediaId"), Col("DepartmentId"), Col("ProtectionId"), Col("Kind"), Col("ContentType"), Col("Width"), Col("Height"),
				Col("ByteSize"), Col("Checksum"), Col("UploadedByUserId"), Col("UploadedOn"), Col("MediaKey"), Col("CreatedOn"), Col("ModifiedOn"), Col("RowVersion")
			});
		}

		public Task<IEnumerable<DepartmentProfileMedia>> GetMetadataForDepartmentAsync(int departmentId)
		{
			return QueryAsync<DepartmentProfileMedia>(
				$"SELECT {MetadataColumns()} FROM {Tbl("DepartmentProfileMedia")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("Kind")}",
				new { DepartmentId = departmentId });
		}

		public Task<DepartmentProfileMedia> GetAsync(int departmentId, int kind)
		{
			return QueryFirstOrDefaultAsync<DepartmentProfileMedia>(
				$"SELECT * FROM {Tbl("DepartmentProfileMedia")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Kind")} = {P}Kind",
				new { DepartmentId = departmentId, Kind = kind });
		}

		public Task<DepartmentProfileMedia> GetByMediaKeyAsync(string mediaKey, int kind)
		{
			return QueryFirstOrDefaultAsync<DepartmentProfileMedia>(
				$"SELECT * FROM {Tbl("DepartmentProfileMedia")} WHERE {Col("MediaKey")} = {P}MediaKey AND {Col("Kind")} = {P}Kind",
				new { MediaKey = mediaKey, Kind = kind });
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("DepartmentProfileMedia")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);
		}

		public Task<int> UpdateMediaKeyAsync(int departmentId, string mediaKey, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("DepartmentProfileMedia")} SET {Col("MediaKey")} = {P}MediaKey, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId, MediaKey = mediaKey, Now = System.DateTime.UtcNow }, cancellationToken);
		}
	}

	public class RmsRecordPrintLayoutsRepository : RmsRepositoryBase<RmsRecordPrintLayout>, IRmsRecordPrintLayoutsRepository
	{
		public RmsRecordPrintLayoutsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordPrintLayout> GetAsync(int departmentId, int scope, string definitionKey)
		{
			return QueryFirstOrDefaultAsync<RmsRecordPrintLayout>(
				$"SELECT * FROM {Tbl("RmsRecordPrintLayouts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Scope")} = {P}Scope AND {Col("DefinitionKey")} = {P}DefinitionKey",
				new { DepartmentId = departmentId, Scope = scope, DefinitionKey = definitionKey ?? string.Empty });
		}
	}
}
