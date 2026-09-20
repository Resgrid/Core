using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Workforce;

namespace Resgrid.Model.Repositories
{
	// Workforce & Business Operations plan, Phase E (E2/E3): repositories for M0220–M0223. Every query is
	// department-scoped; protected columns come back as envelopes and are resolved by the services' seams.

	#region M0220 employment

	public interface IWorkforceEmployerProfileRepository : IRepository<WorkforceEmployerProfile>
	{
		Task<WorkforceEmployerProfile> GetActiveForDepartmentAsync(int departmentId);
		Task<IEnumerable<WorkforceEmployerProfile>> GetForDepartmentAsync(int departmentId);
		/// <summary>Departments with an active employer profile (worker 49 readiness sweep).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithActiveProfilesAsync();
	}

	public interface IWorkforceAffiliatedEntityRepository : IRepository<WorkforceAffiliatedEntity>
	{
		Task<WorkforceAffiliatedEntity> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceAffiliatedEntity>> GetForDepartmentAsync(int departmentId);
	}

	public interface IWorkforceEstablishmentRepository : IRepository<WorkforceEstablishment>
	{
		Task<WorkforceEstablishment> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceEstablishment>> GetForDepartmentAsync(int departmentId);
	}

	public interface IWorkforceLaborContractorRepository : IRepository<WorkforceLaborContractor>
	{
		Task<WorkforceLaborContractor> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceLaborContractor>> GetForDepartmentAsync(int departmentId);
	}

	public interface IWorkforceWorkerRepository : IRepository<WorkforceWorker>
	{
		Task<WorkforceWorker> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<WorkforceWorker> GetByUserIdAsync(int departmentId, string userId);
		Task<IEnumerable<WorkforceWorker>> GetForDepartmentAsync(int departmentId);
	}

	public interface IWorkforceEmploymentRepository : IRepository<WorkforceEmployment>
	{
		Task<WorkforceEmployment> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceEmployment>> GetByWorkerAsync(string workerId);
		Task<IEnumerable<WorkforceEmployment>> GetForDepartmentAsync(int departmentId);
		/// <summary>Employments whose period overlaps the window.</summary>
		Task<IEnumerable<WorkforceEmployment>> GetActiveInWindowAsync(int departmentId, DateTime from, DateTime to);
	}

	public interface IWorkforceJobAssignmentRepository : IRepository<WorkforceJobAssignment>
	{
		Task<WorkforceJobAssignment> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceJobAssignment>> GetByEmploymentAsync(string employmentId);
		Task<IEnumerable<WorkforceJobAssignment>> GetByEmploymentsAsync(IEnumerable<string> employmentIds);
	}

	#endregion

	#region M0221 compensation

	public interface IEmployeeCompensationProfileRepository : IRepository<EmployeeCompensationProfile>
	{
		Task<EmployeeCompensationProfile> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<EmployeeCompensationProfile>> GetByEmploymentAsync(string employmentId);
		Task<IEnumerable<EmployeeCompensationProfile>> GetDefaultsForDepartmentAsync(int departmentId);
		Task<IEnumerable<EmployeeCompensationProfile>> GetByEmploymentsAsync(IEnumerable<string> employmentIds);
	}

	public interface IEmployeePayComponentRepository : IRepository<EmployeePayComponent>
	{
		Task<IEnumerable<EmployeePayComponent>> GetByProfileAsync(string profileId);
		Task<IEnumerable<EmployeePayComponent>> GetByProfilesAsync(IEnumerable<string> profileIds);
	}

	public interface IEmployeeCostComponentRepository : IRepository<EmployeeCostComponent>
	{
		Task<IEnumerable<EmployeeCostComponent>> GetByProfileAsync(string profileId);
		Task<IEnumerable<EmployeeCostComponent>> GetByProfilesAsync(IEnumerable<string> profileIds);
	}

	public interface IWorkforceWorkEntryRepository : IRepository<WorkforceWorkEntry>
	{
		Task<WorkforceWorkEntry> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceWorkEntry>> GetByWorkerAsync(string workerId, DateTime from, DateTime to);
		Task<IEnumerable<WorkforceWorkEntry>> GetByDeploymentAsync(string deploymentId);
		Task<IEnumerable<WorkforceWorkEntry>> GetByCallAsync(int callId);
		Task<IEnumerable<WorkforceWorkEntry>> GetForDepartmentInWindowAsync(int departmentId, DateTime from, DateTime to);
		Task<WorkforceWorkEntry> GetByExternalIdAsync(int departmentId, string externalSource, string externalId);
	}

	public interface IWorkforceAnnualPayFactRepository : IRepository<WorkforceAnnualPayFact>
	{
		Task<WorkforceAnnualPayFact> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<WorkforceAnnualPayFact>> GetForYearAsync(int departmentId, int reportingYear, int reportType);
		Task<IEnumerable<WorkforceAnnualPayFact>> GetByEmploymentAsync(string employmentId);
	}

	#endregion

	#region M0222 costing

	public interface IResourceCostProfileRepository : IRepository<ResourceCostProfile>
	{
		Task<ResourceCostProfile> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<ResourceCostProfile>> GetForDepartmentAsync(int departmentId);
		Task<IEnumerable<ResourceCostProfile>> GetByUnitIdsAsync(int departmentId, IEnumerable<int> unitIds);
	}

	public interface IResourceCostComponentRepository : IRepository<ResourceCostComponent>
	{
		Task<IEnumerable<ResourceCostComponent>> GetByProfileAsync(string profileId);
		Task<IEnumerable<ResourceCostComponent>> GetByProfilesAsync(IEnumerable<string> profileIds);
	}

	public interface IResourceUsageEntryRepository : IRepository<ResourceUsageEntry>
	{
		Task<ResourceUsageEntry> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<ResourceUsageEntry>> GetByDeploymentAsync(string deploymentId);
		Task<IEnumerable<ResourceUsageEntry>> GetByCallAsync(int callId);
		Task<IEnumerable<ResourceUsageEntry>> GetByUnitAsync(int departmentId, int unitId, DateTime from, DateTime to);
	}

	public interface IFieldCostRunRepository : IRepository<FieldCostRun>
	{
		Task<FieldCostRun> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<FieldCostRun>> GetByDeploymentAsync(string deploymentId, int departmentId);
		Task<IEnumerable<FieldCostRun>> GetByBidAsync(string bidId, int departmentId);
		Task<IEnumerable<FieldCostRun>> GetByCallAsync(int callId, int departmentId);
		Task<IEnumerable<FieldCostRun>> GetForDepartmentAsync(int departmentId, int skip, int take);
	}

	public interface IFieldCostLineRepository : IRepository<FieldCostLine>
	{
		Task<IEnumerable<FieldCostLine>> GetByRunAsync(string runId);
		Task<bool> DeleteByRunAsync(string runId, CancellationToken cancellationToken = default);
	}

	#endregion

	#region M0223 pay data reporting

	public interface IPayDataReportingDemographicRepository : IRepository<PayDataReportingDemographic>
	{
		Task<PayDataReportingDemographic> GetCurrentForWorkerAsync(string workerId, DateTime asOf);
		Task<IEnumerable<PayDataReportingDemographic>> GetCurrentForDepartmentAsync(int departmentId, DateTime asOf);
	}

	public interface IPayDataReportRunRepository : IRepository<PayDataReportRun>
	{
		Task<PayDataReportRun> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<PayDataReportRun>> GetForDepartmentAsync(int departmentId, int? reportingYear = null);
		Task<IEnumerable<int>> GetDepartmentsWithRunsAsync(int reportingYear);
	}

	public interface IPayDataReportEmployeeSnapshotRepository : IRepository<PayDataReportEmployeeSnapshot>
	{
		Task<IEnumerable<PayDataReportEmployeeSnapshot>> GetByRunAsync(string runId);
		Task<bool> DeleteByRunAsync(string runId, CancellationToken cancellationToken = default);
	}

	public interface IPayDataReportRowRepository : IRepository<PayDataReportRow>
	{
		Task<IEnumerable<PayDataReportRow>> GetByRunAsync(string runId);
		Task<bool> DeleteByRunAsync(string runId, CancellationToken cancellationToken = default);
	}

	public interface IPayDataExportArtifactRepository : IRepository<PayDataExportArtifact>
	{
		Task<PayDataExportArtifact> GetByIdForDepartmentAsync(string id, int departmentId);
		Task<IEnumerable<PayDataExportArtifact>> GetByRunAsync(string runId);
		Task<IEnumerable<PayDataExportArtifact>> GetExpiredUnpurgedAsync(DateTime asOf);
	}

	#endregion
}
