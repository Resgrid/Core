using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Round 9A (M0405-M0406): certification requirement sets and the issuance ledger. Portable SQL over RmsRepositoryBase; member ids are
	// citext on PostgreSQL, so their parameters are cast (pg-citext-text-param-equality); list parameters use InList (postgres-dapper-in-list).

	public class LmsCertificationRequirementSetsRepository : RmsRepositoryBase<LmsCertificationRequirementSet>, ILmsCertificationRequirementSetsRepository
	{
		public LmsCertificationRequirementSetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCertificationRequirementSet>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsCertificationRequirementSet>(
				$"SELECT * FROM {Tbl("LmsCertificationRequirementSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LmsCertificationRequirementSetId")}",
				new { DepartmentId = departmentId })).ToList();

		public Task<LmsCertificationRequirementSet> GetForTypeAsync(int departmentId, int departmentCertificationTypeId) =>
			QueryFirstOrDefaultAsync<LmsCertificationRequirementSet>(
				$"SELECT * FROM {Tbl("LmsCertificationRequirementSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DepartmentCertificationTypeId")} = {P}TypeId",
				new { DepartmentId = departmentId, TypeId = departmentCertificationTypeId });

		public async Task<List<int>> GetDepartmentIdsWithActiveSetsAsync() =>
			(await QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("LmsCertificationRequirementSets")} WHERE {Col("IsActive")} = {(IsPostgres ? "true" : "1")}", null))
				.OrderBy(id => id).ToList();

		public async Task<bool> HasActiveSetsAsync(int departmentId) =>
			await ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl("LmsCertificationRequirementSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsActive")} = {(IsPostgres ? "true" : "1")}",
				new { DepartmentId = departmentId }) > 0;
	}

	public class LmsCertificationRequirementItemsRepository : RmsRepositoryBase<LmsCertificationRequirementItem>, ILmsCertificationRequirementItemsRepository
	{
		public LmsCertificationRequirementItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCertificationRequirementItem>> GetForSetsAsync(int departmentId, IEnumerable<int> setIds)
		{
			var ids = InListValue(setIds);
			if (ids.Length == 0)
				return new List<LmsCertificationRequirementItem>();
			return (await QueryAsync<LmsCertificationRequirementItem>(
				$"SELECT * FROM {Tbl("LmsCertificationRequirementItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsCertificationRequirementSetId", "Ids")} " +
				$"ORDER BY {Col("LmsCertificationRequirementSetId")}, {Col("SortOrder")}, {Col("LmsCertificationRequirementItemId")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<int> DeleteForSetAsync(int departmentId, int setId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsCertificationRequirementItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCertificationRequirementSetId")} = {P}SetId",
				new { DepartmentId = departmentId, SetId = setId }, cancellationToken);
	}

	public class LmsCertificationIssuancesRepository : RmsRepositoryBase<LmsCertificationIssuance>, ILmsCertificationIssuancesRepository
	{
		public LmsCertificationIssuancesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCertificationIssuance>> GetLiveForUserAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<LmsCertificationIssuance>();
			return (await QueryAsync<LmsCertificationIssuance>(
				$"SELECT * FROM {Tbl("LmsCertificationIssuances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("State")} = {(int)LmsCertificationIssuanceStates.Live} ORDER BY {Col("IssuedOn")}, {Col("LmsCertificationIssuanceId")}",
				new { DepartmentId = departmentId, UserId = userId.Trim() })).ToList();
		}

		public async Task<List<LmsCertificationIssuance>> GetForCertificationAsync(int departmentId, int personnelCertificationId) =>
			(await QueryAsync<LmsCertificationIssuance>(
				$"SELECT * FROM {Tbl("LmsCertificationIssuances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PersonnelCertificationId")} = {P}CertificationId " +
				$"ORDER BY {Col("IssuedOn")}, {Col("LmsCertificationIssuanceId")}",
				new { DepartmentId = departmentId, CertificationId = personnelCertificationId })).ToList();

		public async Task<List<LmsCertificationIssuance>> GetRecentForUserAsync(int departmentId, string userId, int take)
		{
			if (string.IsNullOrWhiteSpace(userId) || take <= 0)
				return new List<LmsCertificationIssuance>();
			var limit = IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";
			return (await QueryAsync<LmsCertificationIssuance>(
				$"SELECT * FROM {Tbl("LmsCertificationIssuances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("IssuedOn")} DESC, {Col("LmsCertificationIssuanceId")} DESC {limit}",
				new { DepartmentId = departmentId, UserId = userId.Trim(), Take = Math.Min(take, 500) })).ToList();
		}

		public async Task<bool> UpdateStateAsync(LmsCertificationIssuance issuance, CancellationToken cancellationToken = default)
		{
			if (issuance == null || issuance.LmsCertificationIssuanceId <= 0)
				return false;
			var parameters = new DynamicParameters();
			parameters.Add("Id", issuance.LmsCertificationIssuanceId);
			parameters.Add("DepartmentId", issuance.DepartmentId);
			parameters.Add("State", issuance.State);
			parameters.Add("Action", issuance.Action);
			parameters.Add("BasisRecordIds", issuance.BasisRecordIds);
			parameters.Add("NewExpiresOn", issuance.NewExpiresOn.HasValue ? DatabaseTimestamp(issuance.NewExpiresOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("PreviousStatus", issuance.PreviousStatus, System.Data.DbType.Int32);
			parameters.Add("PreviousExpiresOn", issuance.PreviousExpiresOn.HasValue ? DatabaseTimestamp(issuance.PreviousExpiresOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("PreviousRecievedOn", issuance.PreviousRecievedOn.HasValue ? DatabaseTimestamp(issuance.PreviousRecievedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("RolledBackOn", issuance.RolledBackOn.HasValue ? DatabaseTimestamp(issuance.RolledBackOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("RolledBackByUserId", issuance.RolledBackByUserId);
			parameters.Add("RollbackReason", issuance.RollbackReason);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsCertificationIssuances")} SET {Col("State")} = {P}State, {Col("Action")} = {P}Action, {Col("BasisRecordIds")} = {P}BasisRecordIds, " +
				$"{Col("NewExpiresOn")} = {P}NewExpiresOn, {Col("PreviousStatus")} = {P}PreviousStatus, {Col("PreviousExpiresOn")} = {P}PreviousExpiresOn, " +
				$"{Col("PreviousRecievedOn")} = {P}PreviousRecievedOn, {Col("RolledBackOn")} = {P}RolledBackOn, {Col("RolledBackByUserId")} = {P}RolledBackByUserId, " +
				$"{Col("RollbackReason")} = {P}RollbackReason WHERE {Col("LmsCertificationIssuanceId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId",
				parameters, cancellationToken) == 1;
		}
	}
}
