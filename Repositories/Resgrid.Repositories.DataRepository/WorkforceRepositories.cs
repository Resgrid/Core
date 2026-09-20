using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Workforce;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Workforce & Business Operations plan, Phase E (E2/E3): repositories for M0220–M0223 (dual-dialect Dapper).

	internal static class WorkforceSql
	{
		public static string False => Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres ? "FALSE" : "0";
		public static string True => Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres ? "TRUE" : "1";
	}

	public class WorkforceEmployerProfileRepository : RmsRepositoryBase<WorkforceEmployerProfile>, IWorkforceEmployerProfileRepository
	{
		public WorkforceEmployerProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceEmployerProfile> GetActiveForDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceEmployerProfile>($"SELECT * FROM {Tbl("WorkforceEmployerProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("IsActive")} = {WorkforceSql.True} ORDER BY {Col("RowVersion")} DESC", new { DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceEmployerProfile>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceEmployerProfile>($"SELECT * FROM {Tbl("WorkforceEmployerProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")} DESC", new { DepartmentId = departmentId });
		public Task<IEnumerable<int>> GetDepartmentsWithActiveProfilesAsync() =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("WorkforceEmployerProfiles")} WHERE {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("IsActive")} = {WorkforceSql.True}", new { });
	}
	public class WorkforceAffiliatedEntityRepository : RmsRepositoryBase<WorkforceAffiliatedEntity>, IWorkforceAffiliatedEntityRepository
	{
		public WorkforceAffiliatedEntityRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceAffiliatedEntity> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceAffiliatedEntity>($"SELECT * FROM {Tbl("WorkforceAffiliatedEntities")} WHERE {Col("WorkforceAffiliatedEntityId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceAffiliatedEntity>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceAffiliatedEntity>($"SELECT * FROM {Tbl("WorkforceAffiliatedEntities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("LegalName")}", new { DepartmentId = departmentId });
	}
	public class WorkforceEstablishmentRepository : RmsRepositoryBase<WorkforceEstablishment>, IWorkforceEstablishmentRepository
	{
		public WorkforceEstablishmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceEstablishment> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceEstablishment>($"SELECT * FROM {Tbl("WorkforceEstablishments")} WHERE {Col("WorkforceEstablishmentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceEstablishment>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceEstablishment>($"SELECT * FROM {Tbl("WorkforceEstablishments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Code")}", new { DepartmentId = departmentId });
	}
	public class WorkforceLaborContractorRepository : RmsRepositoryBase<WorkforceLaborContractor>, IWorkforceLaborContractorRepository
	{
		public WorkforceLaborContractorRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceLaborContractor> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceLaborContractor>($"SELECT * FROM {Tbl("WorkforceLaborContractors")} WHERE {Col("WorkforceLaborContractorId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceLaborContractor>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceLaborContractor>($"SELECT * FROM {Tbl("WorkforceLaborContractors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("LegalName")}", new { DepartmentId = departmentId });
	}
	public class WorkforceWorkerRepository : RmsRepositoryBase<WorkforceWorker>, IWorkforceWorkerRepository
	{
		public WorkforceWorkerRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceWorker> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceWorker>($"SELECT * FROM {Tbl("WorkforceWorkers")} WHERE {Col("WorkforceWorkerId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<WorkforceWorker> GetByUserIdAsync(int departmentId, string userId) =>
			QueryFirstOrDefaultAsync<WorkforceWorker>($"SELECT * FROM {Tbl("WorkforceWorkers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId AND {Col("IsDeleted")} = {WorkforceSql.False}", new { DepartmentId = departmentId, UserId = userId });
		public Task<IEnumerable<WorkforceWorker>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceWorker>($"SELECT * FROM {Tbl("WorkforceWorkers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")}", new { DepartmentId = departmentId });
	}
	public class WorkforceEmploymentRepository : RmsRepositoryBase<WorkforceEmployment>, IWorkforceEmploymentRepository
	{
		public WorkforceEmploymentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceEmployment> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceEmployment>($"SELECT * FROM {Tbl("WorkforceEmployments")} WHERE {Col("WorkforceEmploymentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceEmployment>> GetByWorkerAsync(string id) =>
			QueryAsync<WorkforceEmployment>($"SELECT * FROM {Tbl("WorkforceEmployments")} WHERE {Col("WorkforceWorkerId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("StartOn")}", new { Id = id });
		public Task<IEnumerable<WorkforceEmployment>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<WorkforceEmployment>($"SELECT * FROM {Tbl("WorkforceEmployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("StartOn")}", new { DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceEmployment>> GetActiveInWindowAsync(int departmentId, DateTime from, DateTime to) =>
			QueryAsync<WorkforceEmployment>($"SELECT * FROM {Tbl("WorkforceEmployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("StartOn")} <= {P}To AND ({Col("EndOn")} IS NULL OR {Col("EndOn")} >= {P}From) ORDER BY {Col("StartOn")}",
				new { DepartmentId = departmentId, From = DatabaseTimestamp(from), To = DatabaseTimestamp(to) });
	}
	public class WorkforceJobAssignmentRepository : RmsRepositoryBase<WorkforceJobAssignment>, IWorkforceJobAssignmentRepository
	{
		public WorkforceJobAssignmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceJobAssignment> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceJobAssignment>($"SELECT * FROM {Tbl("WorkforceJobAssignments")} WHERE {Col("WorkforceJobAssignmentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceJobAssignment>> GetByEmploymentAsync(string id) =>
			QueryAsync<WorkforceJobAssignment>($"SELECT * FROM {Tbl("WorkforceJobAssignments")} WHERE {Col("WorkforceEmploymentId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("EffectiveOn")}", new { Id = id });
		public Task<IEnumerable<WorkforceJobAssignment>> GetByEmploymentsAsync(IEnumerable<string> ids)
		{
			var list = InListValue(ids);
			if (list.Length == 0) return Task.FromResult(Enumerable.Empty<WorkforceJobAssignment>());
			return QueryAsync<WorkforceJobAssignment>($"SELECT * FROM {Tbl("WorkforceJobAssignments")} WHERE {InList("WorkforceEmploymentId", "Ids")} AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("EffectiveOn")}", new { Ids = list });
		}
	}
	public class EmployeeCompensationProfileRepository : RmsRepositoryBase<EmployeeCompensationProfile>, IEmployeeCompensationProfileRepository
	{
		public EmployeeCompensationProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<EmployeeCompensationProfile> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<EmployeeCompensationProfile>($"SELECT * FROM {Tbl("EmployeeCompensationProfiles")} WHERE {Col("EmployeeCompensationProfileId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<EmployeeCompensationProfile>> GetByEmploymentAsync(string id) =>
			QueryAsync<EmployeeCompensationProfile>($"SELECT * FROM {Tbl("EmployeeCompensationProfiles")} WHERE {Col("WorkforceEmploymentId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("EffectiveOn")} DESC", new { Id = id });
		public Task<IEnumerable<EmployeeCompensationProfile>> GetDefaultsForDepartmentAsync(int departmentId) =>
			QueryAsync<EmployeeCompensationProfile>($"SELECT * FROM {Tbl("EmployeeCompensationProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("Scope")} <> {P}Employee ORDER BY {Col("Scope")}, {Col("EffectiveOn")} DESC", new { DepartmentId = departmentId, Employee = (int)CompensationScopes.Employee });
		public Task<IEnumerable<EmployeeCompensationProfile>> GetByEmploymentsAsync(IEnumerable<string> ids)
		{
			var list = InListValue(ids);
			if (list.Length == 0) return Task.FromResult(Enumerable.Empty<EmployeeCompensationProfile>());
			return QueryAsync<EmployeeCompensationProfile>($"SELECT * FROM {Tbl("EmployeeCompensationProfiles")} WHERE {InList("WorkforceEmploymentId", "Ids")} AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("EffectiveOn")} DESC", new { Ids = list });
		}
	}
	public class EmployeePayComponentRepository : RmsRepositoryBase<EmployeePayComponent>, IEmployeePayComponentRepository
	{
		public EmployeePayComponentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<EmployeePayComponent>> GetByProfileAsync(string id) =>
			QueryAsync<EmployeePayComponent>($"SELECT * FROM {Tbl("EmployeePayComponents")} WHERE {Col("EmployeeCompensationProfileId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Id = id });
		public Task<IEnumerable<EmployeePayComponent>> GetByProfilesAsync(IEnumerable<string> ids)
		{
			var list = InListValue(ids);
			if (list.Length == 0) return Task.FromResult(Enumerable.Empty<EmployeePayComponent>());
			return QueryAsync<EmployeePayComponent>($"SELECT * FROM {Tbl("EmployeePayComponents")} WHERE {InList("EmployeeCompensationProfileId", "Ids")} AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Ids = list });
		}
	}
	public class EmployeeCostComponentRepository : RmsRepositoryBase<EmployeeCostComponent>, IEmployeeCostComponentRepository
	{
		public EmployeeCostComponentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<EmployeeCostComponent>> GetByProfileAsync(string id) =>
			QueryAsync<EmployeeCostComponent>($"SELECT * FROM {Tbl("EmployeeCostComponents")} WHERE {Col("EmployeeCompensationProfileId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Id = id });
		public Task<IEnumerable<EmployeeCostComponent>> GetByProfilesAsync(IEnumerable<string> ids)
		{
			var list = InListValue(ids);
			if (list.Length == 0) return Task.FromResult(Enumerable.Empty<EmployeeCostComponent>());
			return QueryAsync<EmployeeCostComponent>($"SELECT * FROM {Tbl("EmployeeCostComponents")} WHERE {InList("EmployeeCompensationProfileId", "Ids")} AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Ids = list });
		}
	}
	public class WorkforceWorkEntryRepository : RmsRepositoryBase<WorkforceWorkEntry>, IWorkforceWorkEntryRepository
	{
		public WorkforceWorkEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceWorkEntry> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("WorkforceWorkEntryId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceWorkEntry>> GetByWorkerAsync(string workerId, DateTime from, DateTime to) =>
			QueryAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("WorkforceWorkerId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("WorkDate")} >= {P}From AND {Col("WorkDate")} <= {P}To ORDER BY {Col("WorkDate")}", new { Id = workerId, From = DatabaseTimestamp(from), To = DatabaseTimestamp(to) });
		public Task<IEnumerable<WorkforceWorkEntry>> GetByDeploymentAsync(string id) =>
			QueryAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("WorkDate")}", new { Id = id });
		public Task<IEnumerable<WorkforceWorkEntry>> GetByCallAsync(int callId) =>
			QueryAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("CallId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("WorkDate")}", new { Id = callId });
		public Task<IEnumerable<WorkforceWorkEntry>> GetForDepartmentInWindowAsync(int departmentId, DateTime from, DateTime to) =>
			QueryAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("WorkDate")} >= {P}From AND {Col("WorkDate")} <= {P}To ORDER BY {Col("WorkDate")}", new { DepartmentId = departmentId, From = DatabaseTimestamp(from), To = DatabaseTimestamp(to) });
		public Task<WorkforceWorkEntry> GetByExternalIdAsync(int departmentId, string externalSource, string externalId) =>
			QueryFirstOrDefaultAsync<WorkforceWorkEntry>($"SELECT * FROM {Tbl("WorkforceWorkEntries")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ExternalSource")} = {P}Source AND {Col("ExternalId")} = {P}ExternalId AND {Col("IsDeleted")} = {WorkforceSql.False}", new { DepartmentId = departmentId, Source = externalSource, ExternalId = externalId });
	}
	public class WorkforceAnnualPayFactRepository : RmsRepositoryBase<WorkforceAnnualPayFact>, IWorkforceAnnualPayFactRepository
	{
		public WorkforceAnnualPayFactRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceAnnualPayFact> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceAnnualPayFact>($"SELECT * FROM {Tbl("WorkforceAnnualPayFacts")} WHERE {Col("WorkforceAnnualPayFactId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<WorkforceAnnualPayFact>> GetForYearAsync(int departmentId, int reportingYear, int reportType) =>
			QueryAsync<WorkforceAnnualPayFact>($"SELECT * FROM {Tbl("WorkforceAnnualPayFacts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ReportingYear")} = {P}Year AND {Col("ReportType")} = {P}Type AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("WorkforceEmploymentId")}, {Col("Version")} DESC", new { DepartmentId = departmentId, Year = reportingYear, Type = reportType });
		public Task<IEnumerable<WorkforceAnnualPayFact>> GetByEmploymentAsync(string id) =>
			QueryAsync<WorkforceAnnualPayFact>($"SELECT * FROM {Tbl("WorkforceAnnualPayFacts")} WHERE {Col("WorkforceEmploymentId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("ReportingYear")} DESC, {Col("Version")} DESC", new { Id = id });
	}
	public class ResourceCostProfileRepository : RmsRepositoryBase<ResourceCostProfile>, IResourceCostProfileRepository
	{
		public ResourceCostProfileRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ResourceCostProfile> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<ResourceCostProfile>($"SELECT * FROM {Tbl("ResourceCostProfiles")} WHERE {Col("ResourceCostProfileId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<ResourceCostProfile>> GetForDepartmentAsync(int departmentId) =>
			QueryAsync<ResourceCostProfile>($"SELECT * FROM {Tbl("ResourceCostProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Name")}, {Col("EffectiveOn")} DESC", new { DepartmentId = departmentId });
		public Task<IEnumerable<ResourceCostProfile>> GetByUnitIdsAsync(int departmentId, IEnumerable<int> unitIds)
		{
			var ids = InListValue(unitIds);
			if (ids.Length == 0) return Task.FromResult(Enumerable.Empty<ResourceCostProfile>());
			return QueryAsync<ResourceCostProfile>($"SELECT * FROM {Tbl("ResourceCostProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {InList("UnitId", "Ids")} ORDER BY {Col("EffectiveOn")} DESC", new { DepartmentId = departmentId, Ids = ids });
		}
	}
	public class ResourceCostComponentRepository : RmsRepositoryBase<ResourceCostComponent>, IResourceCostComponentRepository
	{
		public ResourceCostComponentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<ResourceCostComponent>> GetByProfileAsync(string id) =>
			QueryAsync<ResourceCostComponent>($"SELECT * FROM {Tbl("ResourceCostComponents")} WHERE {Col("ResourceCostProfileId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Id = id });
		public Task<IEnumerable<ResourceCostComponent>> GetByProfilesAsync(IEnumerable<string> ids)
		{
			var list = InListValue(ids);
			if (list.Length == 0) return Task.FromResult(Enumerable.Empty<ResourceCostComponent>());
			return QueryAsync<ResourceCostComponent>($"SELECT * FROM {Tbl("ResourceCostComponents")} WHERE {InList("ResourceCostProfileId", "Ids")} AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("Category")}", new { Ids = list });
		}
	}
	public class ResourceUsageEntryRepository : RmsRepositoryBase<ResourceUsageEntry>, IResourceUsageEntryRepository
	{
		public ResourceUsageEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ResourceUsageEntry> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<ResourceUsageEntry>($"SELECT * FROM {Tbl("ResourceUsageEntries")} WHERE {Col("ResourceUsageEntryId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<ResourceUsageEntry>> GetByDeploymentAsync(string id) =>
			QueryAsync<ResourceUsageEntry>($"SELECT * FROM {Tbl("ResourceUsageEntries")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("UsageDate")}", new { Id = id });
		public Task<IEnumerable<ResourceUsageEntry>> GetByCallAsync(int callId) =>
			QueryAsync<ResourceUsageEntry>($"SELECT * FROM {Tbl("ResourceUsageEntries")} WHERE {Col("CallId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("UsageDate")}", new { Id = callId });
		public Task<IEnumerable<ResourceUsageEntry>> GetByUnitAsync(int departmentId, int unitId, DateTime from, DateTime to) =>
			QueryAsync<ResourceUsageEntry>($"SELECT * FROM {Tbl("ResourceUsageEntries")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UnitId")} = {P}UnitId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("UsageDate")} >= {P}From AND {Col("UsageDate")} <= {P}To ORDER BY {Col("UsageDate")}", new { DepartmentId = departmentId, UnitId = unitId, From = DatabaseTimestamp(from), To = DatabaseTimestamp(to) });
	}
	public class FieldCostRunRepository : RmsRepositoryBase<FieldCostRun>, IFieldCostRunRepository
	{
		public FieldCostRunRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<FieldCostRun> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<FieldCostRun>($"SELECT * FROM {Tbl("FieldCostRuns")} WHERE {Col("FieldCostRunId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<FieldCostRun>> GetByDeploymentAsync(string deploymentId, int departmentId) =>
			QueryAsync<FieldCostRun>($"SELECT * FROM {Tbl("FieldCostRuns")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")} DESC", new { Id = deploymentId, DepartmentId = departmentId });
		public Task<IEnumerable<FieldCostRun>> GetByBidAsync(string bidId, int departmentId) =>
			QueryAsync<FieldCostRun>($"SELECT * FROM {Tbl("FieldCostRuns")} WHERE {Col("BidId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")} DESC", new { Id = bidId, DepartmentId = departmentId });
		public Task<IEnumerable<FieldCostRun>> GetByCallAsync(int callId, int departmentId) =>
			QueryAsync<FieldCostRun>($"SELECT * FROM {Tbl("FieldCostRuns")} WHERE {Col("CallId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")} DESC", new { Id = callId, DepartmentId = departmentId });
		public Task<IEnumerable<FieldCostRun>> GetForDepartmentAsync(int departmentId, int skip, int take) =>
			QueryAsync<FieldCostRun>($"SELECT * FROM {Tbl("FieldCostRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} ORDER BY {Col("AddedOn")} DESC {Paging()}", new { DepartmentId = departmentId, Skip = skip, Take = take });
	}
	public class FieldCostLineRepository : RmsRepositoryBase<FieldCostLine>, IFieldCostLineRepository
	{
		public FieldCostLineRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<FieldCostLine>> GetByRunAsync(string id) =>
			QueryAsync<FieldCostLine>($"SELECT * FROM {Tbl("FieldCostLines")} WHERE {Col("FieldCostRunId")} = {P}Id ORDER BY {Col("SortOrder")}", new { Id = id });
		public async Task<bool> DeleteByRunAsync(string id, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("FieldCostLines")} WHERE {Col("FieldCostRunId")} = {P}Id", new { Id = id }, cancellationToken);
			return true;
		}
	}
	public class PayDataReportingDemographicRepository : RmsRepositoryBase<PayDataReportingDemographic>, IPayDataReportingDemographicRepository
	{
		public PayDataReportingDemographicRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<PayDataReportingDemographic> GetCurrentForWorkerAsync(string workerId, DateTime asOf) =>
			QueryFirstOrDefaultAsync<PayDataReportingDemographic>($"SELECT * FROM {Tbl("PayDataReportingDemographics")} WHERE {Col("WorkforceWorkerId")} = {P}Id AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("EffectiveOn")} <= {P}AsOf AND ({Col("ExpiresOn")} IS NULL OR {Col("ExpiresOn")} >= {P}AsOf) ORDER BY {Col("EffectiveOn")} DESC, {Col("Version")} DESC", new { Id = workerId, AsOf = DatabaseTimestamp(asOf) });
		public Task<IEnumerable<PayDataReportingDemographic>> GetCurrentForDepartmentAsync(int departmentId, DateTime asOf) =>
			QueryAsync<PayDataReportingDemographic>($"SELECT * FROM {Tbl("PayDataReportingDemographics")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False} AND {Col("EffectiveOn")} <= {P}AsOf AND ({Col("ExpiresOn")} IS NULL OR {Col("ExpiresOn")} >= {P}AsOf) ORDER BY {Col("WorkforceWorkerId")}, {Col("EffectiveOn")} DESC", new { DepartmentId = departmentId, AsOf = DatabaseTimestamp(asOf) });
	}
	public class PayDataReportRunRepository : RmsRepositoryBase<PayDataReportRun>, IPayDataReportRunRepository
	{
		public PayDataReportRunRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<PayDataReportRun> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<PayDataReportRun>($"SELECT * FROM {Tbl("PayDataReportRuns")} WHERE {Col("PayDataReportRunId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<PayDataReportRun>> GetForDepartmentAsync(int departmentId, int? reportingYear = null) =>
			QueryAsync<PayDataReportRun>($"SELECT * FROM {Tbl("PayDataReportRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {WorkforceSql.False}" + (reportingYear.HasValue ? $" AND {Col("ReportingYear")} = {P}Year" : string.Empty) + $" ORDER BY {Col("ReportingYear")} DESC, {Col("AddedOn")} DESC", new { DepartmentId = departmentId, Year = reportingYear });
		public Task<IEnumerable<int>> GetDepartmentsWithRunsAsync(int reportingYear) =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("PayDataReportRuns")} WHERE {Col("ReportingYear")} = {P}Year AND {Col("IsDeleted")} = {WorkforceSql.False}", new { Year = reportingYear });
	}
	public class PayDataReportEmployeeSnapshotRepository : RmsRepositoryBase<PayDataReportEmployeeSnapshot>, IPayDataReportEmployeeSnapshotRepository
	{
		public PayDataReportEmployeeSnapshotRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<PayDataReportEmployeeSnapshot>> GetByRunAsync(string id) =>
			QueryAsync<PayDataReportEmployeeSnapshot>($"SELECT * FROM {Tbl("PayDataReportEmployeeSnapshots")} WHERE {Col("PayDataReportRunId")} = {P}Id ORDER BY {Col("WorkforceWorkerId")}", new { Id = id });
		public async Task<bool> DeleteByRunAsync(string id, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("PayDataReportEmployeeSnapshots")} WHERE {Col("PayDataReportRunId")} = {P}Id", new { Id = id }, cancellationToken);
			return true;
		}
	}
	public class PayDataReportRowRepository : RmsRepositoryBase<PayDataReportRow>, IPayDataReportRowRepository
	{
		public PayDataReportRowRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<PayDataReportRow>> GetByRunAsync(string id) =>
			QueryAsync<PayDataReportRow>($"SELECT * FROM {Tbl("PayDataReportRows")} WHERE {Col("PayDataReportRunId")} = {P}Id ORDER BY {Col("SortOrder")}", new { Id = id });
		public async Task<bool> DeleteByRunAsync(string id, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("PayDataReportRows")} WHERE {Col("PayDataReportRunId")} = {P}Id", new { Id = id }, cancellationToken);
			return true;
		}
	}
	public class PayDataExportArtifactRepository : RmsRepositoryBase<PayDataExportArtifact>, IPayDataExportArtifactRepository
	{
		public PayDataExportArtifactRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<PayDataExportArtifact> GetByIdForDepartmentAsync(string id, int departmentId) =>
			QueryFirstOrDefaultAsync<PayDataExportArtifact>($"SELECT * FROM {Tbl("PayDataExportArtifacts")} WHERE {Col("PayDataExportArtifactId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = id, DepartmentId = departmentId });
		public Task<IEnumerable<PayDataExportArtifact>> GetByRunAsync(string id) =>
			QueryAsync<PayDataExportArtifact>($"SELECT * FROM {Tbl("PayDataExportArtifacts")} WHERE {Col("PayDataReportRunId")} = {P}Id ORDER BY {Col("CreatedOn")} DESC", new { Id = id });
		public Task<IEnumerable<PayDataExportArtifact>> GetExpiredUnpurgedAsync(DateTime asOf) =>
			QueryAsync<PayDataExportArtifact>($"SELECT * FROM {Tbl("PayDataExportArtifacts")} WHERE {Col("PurgedOn")} IS NULL AND {Col("ExpiresOn")} < {P}AsOf", new { AsOf = DatabaseTimestamp(asOf) });
	}
}
