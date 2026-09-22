using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Workforce;

namespace Resgrid.Model.Services
{
	// Workforce & Business Operations plan, Phase E (E3): the five Phase E services. Every value that identifies or
	// prices a person is written and read through the ADP seam (catalog 28); callers authorize (permissions 74–78).

	/// <summary>Employer identity, affiliates, establishments, labor contractors, workers, employment periods, job assignments, work entries and annual pay facts (plus CSV imports with dry-run).</summary>
	public interface IWorkforceService
	{
		Task<WorkforceEmployerProfile> GetEmployerProfileAsync(int departmentId);
		Task<WorkforceEmployerProfile> SaveEmployerProfileAsync(WorkforceEmployerProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceAffiliatedEntity>> GetAffiliatesAsync(int departmentId);
		Task<WorkforceAffiliatedEntity> SaveAffiliateAsync(WorkforceAffiliatedEntity entity, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteAffiliateAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceEstablishment>> GetEstablishmentsAsync(int departmentId);
		Task<WorkforceEstablishment> GetEstablishmentAsync(string id, int departmentId);
		Task<WorkforceEstablishment> SaveEstablishmentAsync(WorkforceEstablishment establishment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteEstablishmentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceLaborContractor>> GetLaborContractorsAsync(int departmentId);
		Task<WorkforceLaborContractor> SaveLaborContractorAsync(WorkforceLaborContractor contractor, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteLaborContractorAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceWorker>> GetWorkersAsync(int departmentId);
		Task<WorkforceWorker> GetWorkerAsync(string id, int departmentId);
		/// <summary>The worker row for a Resgrid user, created on first use.</summary>
		Task<WorkforceWorker> GetOrCreateWorkerForUserAsync(int departmentId, string userId, string actorUserId, CancellationToken cancellationToken = default);
		Task<WorkforceWorker> SaveWorkerAsync(WorkforceWorker worker, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceEmployment>> GetEmploymentsAsync(int departmentId);
		Task<List<WorkforceEmployment>> GetEmploymentsForWorkerAsync(string workerId, int departmentId);
		Task<WorkforceEmployment> GetEmploymentAsync(string id, int departmentId);
		/// <summary>Validates that a worker's periods never overlap.</summary>
		Task<WorkforceEmployment> SaveEmploymentAsync(WorkforceEmployment employment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteEmploymentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Validates that an employment's assignments never overlap and that the establishment is the department's.</summary>
		Task<WorkforceJobAssignment> SaveJobAssignmentAsync(WorkforceJobAssignment assignment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteJobAssignmentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceWorkEntry>> GetWorkEntriesAsync(int departmentId, DateTime from, DateTime to);
		Task<WorkforceWorkEntry> SaveWorkEntryAsync(WorkforceWorkEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<WorkforceAnnualPayFact>> GetAnnualPayFactsAsync(int departmentId, int reportingYear, PayDataReportTypes reportType);
		/// <summary>Corrections create a new version superseding the current fact for the employment / year / allocation.</summary>
		Task<WorkforceAnnualPayFact> SaveAnnualPayFactAsync(WorkforceAnnualPayFact fact, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Canonical CSV: ExternalWorkerKey|UserId, ReportingYear, ReportType, ClientAllocationKey, W2Box5, W2Box1, ActualWorkedHours, PaidLeaveHours, DaysWorked, WeeksWorked, ExemptProxyMethod, ProxyAverageHoursPerDay, ClientAllocatedEarnings, ClientAllocatedHours, ClientAllocatedWeeks. Dry-run validates and reconciles totals before anything commits.</summary>
		Task<WorkforceImportResult> ImportAnnualPayFactsAsync(int departmentId, string csv, bool dryRun, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}

	/// <summary>Compensation profiles (employee, role default, department default), pay / employer-cost components and the loaded-cost estimate for an approved work quantity.</summary>
	public interface ICompensationCostService
	{
		Task<List<EmployeeCompensationProfile>> GetProfilesForEmploymentAsync(string employmentId, int departmentId);
		Task<List<EmployeeCompensationProfile>> GetDefaultProfilesAsync(int departmentId);
		Task<EmployeeCompensationProfile> GetProfileAsync(string profileId, int departmentId);
		Task<EmployeeCompensationProfile> SaveProfileAsync(EmployeeCompensationProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<EmployeeCompensationProfile> SaveComponentsAsync(string profileId, int departmentId, List<EmployeePayComponent> payComponents, List<EmployeeCostComponent> costComponents, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Saves the profile and replaces its pay / cost components in one transaction: nothing is written unless both succeed.</summary>
		Task<EmployeeCompensationProfile> SaveProfileWithComponentsAsync(EmployeeCompensationProfile profile, List<EmployeePayComponent> payComponents, List<EmployeeCostComponent> costComponents, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<EmployeeCompensationProfile> ApproveProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Employee profile → role default → department default as of the date, decrypted through the costing workload. Null when nothing resolves.</summary>
		Task<(EmployeeCompensationProfile Profile, bool IsFallback)> ResolveProfileAsync(string employmentId, int? personnelRoleId, int departmentId, DateTime asOf);
		Task<LaborCostResult> CalculateLoadedCostAsync(int departmentId, LaborWorkQuantity work, int? personnelRoleId, DateTime asOf, string currency = "USD");
		/// <summary>The narrow aggregate the Cal OES MARS Salary Survey draft may consume: mean of the current actual hourly rates (plus components paid for each overtime hour) per classification — never a pay-range midpoint, never an individual.</summary>
		Task<List<(string ClassificationCode, int Count, decimal MeanRate, decimal MeanOvertimeAdder)>> GetClassificationRateAggregateAsync(int departmentId, DateTime asOf, string calOesAuthorityProfileCode);
	}

	/// <summary>Resource cost profiles / components / usage entries and the internal field-cost runs for bids, calls and deployments.</summary>
	public interface IFieldCostingService
	{
		Task<List<ResourceCostProfile>> GetResourceProfilesAsync(int departmentId);
		Task<ResourceCostProfile> GetResourceProfileAsync(string profileId, int departmentId);
		Task<ResourceCostProfile> SaveResourceProfileAsync(ResourceCostProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<ResourceCostProfile> SaveResourceComponentsAsync(string profileId, int departmentId, List<ResourceCostComponent> components, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteResourceProfileAsync(string profileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<ResourceUsageEntry>> GetUsageForDeploymentAsync(string deploymentId, int departmentId);
		Task<List<ResourceUsageEntry>> GetUsageForCallAsync(int callId, int departmentId);
		/// <summary>Canonicalises distance (km → miles), derives distance / engine hours from meters, and queues conflicting automatic vs manual readings for review.</summary>
		Task<ResourceUsageEntry> SaveUsageEntryAsync(ResourceUsageEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteUsageEntryAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<List<FieldCostRun>> GetRunsAsync(int departmentId, int skip = 0, int take = 100);
		Task<List<FieldCostRun>> GetRunsForDeploymentAsync(string deploymentId, int departmentId);
		Task<List<FieldCostRun>> GetRunsForBidAsync(string bidId, int departmentId);
		Task<List<FieldCostRun>> GetRunsForCallAsync(int callId, int departmentId);
		Task<FieldCostRun> GetRunAsync(string runId, int departmentId);
		/// <summary>Estimate from the bid's lines (personnel hours × role / department default compensation, vehicle hours × resource profile) with revenue = the bid's estimated total.</summary>
		Task<FieldCostRun> EstimateBidCostAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Actual from work entries and resource usage against the call; no revenue.</summary>
		Task<FieldCostRun> CalculateCallCostAsync(int callId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Actual from approved DTR entries (personnel + unit hours), usage entries and expenses through the date; revenue from invoices, the bid estimate or the selected MARS recovery snapshot.</summary>
		Task<FieldCostRun> CalculateDeploymentCostAsync(string deploymentId, int departmentId, DateTime? throughDate, RevenueSources revenueSource, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<FieldCostRun> FreezeCostRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<FieldCostComparison> CompareEstimateToActualAsync(string deploymentId, int departmentId);
		/// <summary>Aggregate categories only (ViewInternalCosts); never a line, rate or person.</summary>
		Task<FieldCostSummary> GetFieldCostSummaryAsync(string runId, int departmentId);
		Task<bool> DeleteRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}

	/// <summary>The separately stored demographic responses: a worker's own self-identification and the compliance officer's completeness view.</summary>
	public interface IPayDataDemographicsService
	{
		Task<PayDataReportingDemographic> GetOwnAsync(int departmentId, string userId);
		Task<PayDataReportingDemographic> SaveOwnAsync(int departmentId, string userId, PayDataReportingDemographic response, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<DemographicCompleteness> GetCompletenessAsync(int departmentId, DateTime asOf);
		/// <summary>A compliance officer's record for a worker (employment record / reliable record / observer perception); requires a reason and is reviewed.</summary>
		Task<PayDataReportingDemographic> SaveForWorkerAsync(int departmentId, string workerId, PayDataReportingDemographic response, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Values for one worker (ManagePayDataReporting + a current grant); the personnel screens never call this.</summary>
		Task<PayDataReportingDemographic> GetForWorkerAsync(int departmentId, string workerId, DateTime asOf);
	}

	/// <summary>The California CRD report wizard (plan E3): create → build snapshots → aggregate → validate → freeze and export → attest.</summary>
	public interface ICaPayDataReportingService
	{
		Task<List<PayDataReportRun>> GetRunsAsync(int departmentId, int? reportingYear = null);
		Task<PayDataReportRun> GetRunAsync(string runId, int departmentId);
		Task<List<PayDataReportEmployeeSnapshot>> GetSnapshotsAsync(string runId, int departmentId);
		Task<List<PayDataReportRow>> GetRowsAsync(string runId, int departmentId);
		Task<PayDataReportRun> CreateRunAsync(int departmentId, int reportingYear, PayDataReportTypes reportType, DateTime snapshotStart, DateTime snapshotEnd, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReportRun> BuildEmployeeSnapshotsAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReportEmployeeSnapshot> OverrideSnapshotAsync(string runId, string snapshotId, int departmentId, bool include, string jobCategoryCode, int? workMode, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReportRun> AggregateRowsAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReportRun> SaveRemarksAsync(string runId, int departmentId, string runRemarks, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataValidationResult> ValidateRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Immutable snapshots + rows, the worksheet and the CSV / XLSX artifacts (checksum-stable for unchanged input).</summary>
		Task<PayDataReportRun> FreezeAndExportAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<List<PayDataExportArtifact>> GetArtifactsAsync(string runId, int departmentId);
		/// <summary>Short-lived, no-store download of an artifact (audited).</summary>
		Task<PayDataExportArtifact> DownloadArtifactAsync(string artifactId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataPortalWorksheet> GetWorksheetAsync(string runId, int departmentId);
		Task<PayDataReportRun> MarkCertifiedExternallyAsync(string runId, int departmentId, string certificationReference, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>A correction supersedes the frozen run with a new draft carrying its settings.</summary>
		Task<PayDataReportRun> CreateCorrectionAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReportRun> VoidRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<PayDataReadiness> GetReadinessAsync(int departmentId, int reportingYear);
		/// <summary>Worker 49: value-free reminder during the filing season, once per department per day.</summary>
		Task<int> RunReadinessSweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);
		Task<int> PurgeExpiredArtifactsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);
	}
}
