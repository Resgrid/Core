using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Workforce & Business Operations plan, Phase C-M3 (C3): Cal OES MARS shadow-table repositories (registry M0219).

	public class CalOesMarsAgencyProfileRepository : RmsRepositoryBase<CalOesMarsAgencyProfile>, ICalOesMarsAgencyProfileRepository
	{
		public CalOesMarsAgencyProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsAgencyProfile> GetByDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsAgencyProfile>($"SELECT * FROM {Tbl("CalOesMarsAgencyProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}", new { DepartmentId = departmentId });
	}

	public class CalOesMarsResourceProfileRepository : RmsRepositoryBase<CalOesMarsResourceProfile>, ICalOesMarsResourceProfileRepository
	{
		public CalOesMarsResourceProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsResourceProfile> GetByIdForDepartmentAsync(string resourceProfileId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsResourceProfile>($"SELECT * FROM {Tbl("CalOesMarsResourceProfiles")} WHERE {Col("CalOesMarsResourceProfileId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = resourceProfileId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsResourceProfile>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<CalOesMarsResourceProfile>($"SELECT * FROM {Tbl("CalOesMarsResourceProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("UnitDesignator")}, {Col("ExternalResourceName")}", new { DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsResourceProfile>> GetByUnitIdsAsync(int departmentId, IEnumerable<int> unitIds)
		{
			var ids = InListValue(unitIds);
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<CalOesMarsResourceProfile>());
			return QueryAsync<CalOesMarsResourceProfile>($"SELECT * FROM {Tbl("CalOesMarsResourceProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} AND {InList("UnitId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}
	}

	public class CalOesMarsRateProfileRepository : RmsRepositoryBase<CalOesMarsRateProfile>, ICalOesMarsRateProfileRepository
	{
		public CalOesMarsRateProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsRateProfile> GetByIdForDepartmentAsync(string rateProfileId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsRateProfile>($"SELECT * FROM {Tbl("CalOesMarsRateProfiles")} WHERE {Col("CalOesMarsRateProfileId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = rateProfileId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsRateProfile>> GetForDepartmentAsync(int departmentId, int? submissionYear = null) =>
			QueryAsync<CalOesMarsRateProfile>(
				$"SELECT * FROM {Tbl("CalOesMarsRateProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}" + (submissionYear.HasValue ? $" AND {Col("SubmissionYear")} = {P}Year" : string.Empty) +
				$" ORDER BY {Col("SubmissionYear")} DESC, {Col("SubmissionType")}, {Col("EffectiveOn")} DESC", new { DepartmentId = departmentId, Year = submissionYear });

		public Task<IEnumerable<CalOesMarsRateProfile>> GetEffectiveAsync(int departmentId, DateTime asOf) =>
			QueryAsync<CalOesMarsRateProfile>(
				$"SELECT * FROM {Tbl("CalOesMarsRateProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} AND {Col("Status")} <> {P}Superseded " +
				$"AND ({Col("EffectiveOn")} IS NULL OR {Col("EffectiveOn")} <= {P}AsOf) AND ({Col("ExpiresOn")} IS NULL OR {Col("ExpiresOn")} >= {P}AsOf) ORDER BY {Col("SubmissionType")}, {Col("EffectiveOn")} DESC",
				new { DepartmentId = departmentId, AsOf = DatabaseTimestamp(asOf.Date), Superseded = (int)CalOesMarsRateProfileStatuses.Superseded });
	}

	public class CalOesMarsRateLineRepository : RmsRepositoryBase<CalOesMarsRateLine>, ICalOesMarsRateLineRepository
	{
		public CalOesMarsRateLineRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsRateLine> GetByIdForDepartmentAsync(string rateLineId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsRateLine>($"SELECT * FROM {Tbl("CalOesMarsRateLines")} WHERE {Col("CalOesMarsRateLineId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = rateLineId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsRateLine>> GetByProfileAsync(string rateProfileId) =>
			QueryAsync<CalOesMarsRateLine>($"SELECT * FROM {Tbl("CalOesMarsRateLines")} WHERE {Col("CalOesMarsRateProfileId")} = {P}Id AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("SortOrder")}, {Col("LineKind")}, {Col("ClassificationCode")}, {Col("ResourceCode")}", new { Id = rateProfileId });

		public Task<IEnumerable<CalOesMarsRateLine>> GetByProfilesAsync(IEnumerable<string> rateProfileIds)
		{
			var ids = InListValue(rateProfileIds);
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<CalOesMarsRateLine>());
			return QueryAsync<CalOesMarsRateLine>($"SELECT * FROM {Tbl("CalOesMarsRateLines")} WHERE {InList("CalOesMarsRateProfileId", "Ids")} AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("SortOrder")}", new { Ids = ids });
		}
	}

	public class CalOesMarsAdministrativeRateInputRepository : RmsRepositoryBase<CalOesMarsAdministrativeRateInput>, ICalOesMarsAdministrativeRateInputRepository
	{
		public CalOesMarsAdministrativeRateInputRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsAdministrativeRateInput> GetByIdForDepartmentAsync(string inputId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsAdministrativeRateInput>($"SELECT * FROM {Tbl("CalOesMarsAdministrativeRateInputs")} WHERE {Col("CalOesMarsAdministrativeRateInputId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = inputId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsAdministrativeRateInput>> GetByProfileAsync(string rateProfileId) =>
			QueryAsync<CalOesMarsAdministrativeRateInput>($"SELECT * FROM {Tbl("CalOesMarsAdministrativeRateInputs")} WHERE {Col("CalOesMarsRateProfileId")} = {P}Id AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("FiscalYear")}, {Col("FunctionCode")}, {Col("CategoryCode")}", new { Id = rateProfileId });
	}

	public class CalOesMarsAgreementSnapshotRepository : RmsRepositoryBase<CalOesMarsAgreementSnapshot>, ICalOesMarsAgreementSnapshotRepository
	{
		public CalOesMarsAgreementSnapshotRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CalOesMarsAgreementSnapshot> GetByIdForDepartmentAsync(string agreementSnapshotId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsAgreementSnapshot>($"SELECT * FROM {Tbl("CalOesMarsAgreementSnapshots")} WHERE {Col("CalOesMarsAgreementSnapshotId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = agreementSnapshotId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsAgreementSnapshot>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<CalOesMarsAgreementSnapshot>($"SELECT * FROM {Tbl("CalOesMarsAgreementSnapshots")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("ClassificationCode")}, {Col("StartOn")} DESC", new { DepartmentId = departmentId });
	}

	public class CalOesMarsWorkItemRepository : RmsRepositoryBase<CalOesMarsWorkItem>, ICalOesMarsWorkItemRepository
	{
		public CalOesMarsWorkItemRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string False => IsPostgres ? "FALSE" : "0";

		public Task<CalOesMarsWorkItem> GetByIdForDepartmentAsync(string workItemId, int departmentId) =>
			QueryFirstOrDefaultAsync<CalOesMarsWorkItem>($"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("CalOesMarsWorkItemId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = workItemId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsWorkItem>> GetByDeploymentAsync(string deploymentId, int departmentId) =>
			QueryAsync<CalOesMarsWorkItem>($"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("DeploymentId")} = {P}DeploymentId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("RecordType")}, {Col("AddedOn")}", new { DeploymentId = deploymentId, DepartmentId = departmentId });

		public Task<IEnumerable<CalOesMarsWorkItem>> GetByExternalIdAsync(int departmentId, string marsRecordId) =>
			QueryAsync<CalOesMarsWorkItem>($"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND ({Col("MarsRecordId")} = {P}ExternalId OR {Col("MarsInvoiceId")} = {P}ExternalId) AND {Col("IsDeleted")} = {False}", new { DepartmentId = departmentId, ExternalId = marsRecordId });

		public Task<IEnumerable<CalOesMarsWorkItem>> GetActionQueueAsync(int departmentId, int? recordType = null) =>
			QueryAsync<CalOesMarsWorkItem>(
				$"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("LocalState")} <> {P}Closed" + (recordType.HasValue ? $" AND {Col("RecordType")} = {P}RecordType" : string.Empty) +
				$" ORDER BY {Col("LocalState")}, {Col("AddedOn")}", new { DepartmentId = departmentId, Closed = (int)CalOesMarsLocalStates.Closed, RecordType = recordType });

		public Task<IEnumerable<CalOesMarsWorkItem>> GetByAgreementSnapshotAsync(int departmentId, string agreementSnapshotId) =>
			QueryAsync<CalOesMarsWorkItem>($"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AgreementSnapshotId")} = {P}AgreementSnapshotId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")}", new { DepartmentId = departmentId, AgreementSnapshotId = agreementSnapshotId });

		public Task<IEnumerable<CalOesMarsWorkItem>> GetUnreconciledAsync(int departmentId) =>
			QueryAsync<CalOesMarsWorkItem>(
				$"SELECT * FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("LocalState")} IN ({P}Submitted, {P}Returned, {P}Approved, {P}PendingLocal, {P}PendingPaying, {P}Rejected) ORDER BY {Col("AddedOn")}",
				new
				{
					DepartmentId = departmentId, Submitted = (int)CalOesMarsLocalStates.SubmittedExternal, Returned = (int)CalOesMarsLocalStates.ReturnedForAgencyReview, Approved = (int)CalOesMarsLocalStates.Approved,
					PendingLocal = (int)CalOesMarsLocalStates.PendingLocalAgencyApproval, PendingPaying = (int)CalOesMarsLocalStates.PendingPayingEntityApproval, Rejected = (int)CalOesMarsLocalStates.LocalAgencyRejected
				});

		public Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync() =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("CalOesMarsWorkItems")} WHERE {Col("IsDeleted")} = {False} AND {Col("LocalState")} <> {P}Closed", new { Closed = (int)CalOesMarsLocalStates.Closed });
	}

	public class CalOesMarsReimbursementLineRepository : RmsRepositoryBase<CalOesMarsReimbursementLine>, ICalOesMarsReimbursementLineRepository
	{
		public CalOesMarsReimbursementLineRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<CalOesMarsReimbursementLine>> GetByWorkItemAsync(string workItemId) =>
			QueryAsync<CalOesMarsReimbursementLine>($"SELECT * FROM {Tbl("CalOesMarsReimbursementLines")} WHERE {Col("CalOesMarsWorkItemId")} = {P}Id ORDER BY {Col("SortOrder")}", new { Id = workItemId });

		public async Task<bool> DeleteByWorkItemAsync(string workItemId, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("CalOesMarsReimbursementLines")} WHERE {Col("CalOesMarsWorkItemId")} = {P}Id", new { Id = workItemId }, cancellationToken);
			return true;
		}
	}
}
