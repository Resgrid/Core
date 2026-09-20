using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model.Workforce;

namespace Resgrid.Web.Areas.User.Models.Workforce
{
	// Workforce & Business Operations plan, Phase E (E6): view models for the workforce workspace. Protected values
	// arrive already resolved by the services (REDACTED without a grant); the views render them with SafeDisplay.

	public class WorkforcePageView
	{
		public bool CanManage { get; set; }
		public bool CanViewCompensation { get; set; }
		public bool CanManageCompensation { get; set; }
		public bool CanViewInternalCosts { get; set; }
		public bool CanViewPayData { get; set; }
		public bool CanManagePayData { get; set; }
		public bool CanExportPayData { get; set; }
		public bool WorkforceEnabled { get; set; }
		public bool PayDataEnabled { get; set; }
		public string Message { get; set; }
		public bool SaveSuccess { get; set; }
	}

	public class WorkforceDashboardView : WorkforcePageView
	{
		public WorkforceEmployerProfile Employer { get; set; }
		public int EstablishmentCount { get; set; }
		public int WorkerCount { get; set; }
		public int EmploymentCount { get; set; }
		public int ResourceProfileCount { get; set; }
		public int CostRunCount { get; set; }
		public PayDataReadiness Readiness { get; set; }
		public DemographicCompleteness Completeness { get; set; }
		public int ReportingYear { get; set; }
	}

	public class WorkforceEmployerView : WorkforcePageView
	{
		public WorkforceEmployerProfile Employer { get; set; }
		public List<WorkforceAffiliatedEntity> Affiliates { get; set; } = new List<WorkforceAffiliatedEntity>();
		public WorkforceAffiliatedEntity EditingAffiliate { get; set; }
	}

	public class WorkforceEstablishmentsView : WorkforcePageView
	{
		public List<WorkforceEstablishment> Establishments { get; set; } = new List<WorkforceEstablishment>();
		public WorkforceEstablishment Editing { get; set; }
		public List<SelectListItem> Affiliates { get; set; } = new List<SelectListItem>();
	}

	public class WorkforceContractorsView : WorkforcePageView
	{
		public List<WorkforceLaborContractor> Contractors { get; set; } = new List<WorkforceLaborContractor>();
		public WorkforceLaborContractor Editing { get; set; }
	}

	public class WorkforceWorkersView : WorkforcePageView
	{
		public List<WorkforceWorker> Workers { get; set; } = new List<WorkforceWorker>();
		public Dictionary<string, int> EmploymentCounts { get; set; } = new Dictionary<string, int>();
		public List<SelectListItem> Members { get; set; } = new List<SelectListItem>();
	}

	public class WorkforceWorkerView : WorkforcePageView
	{
		public WorkforceWorker Worker { get; set; }
		public List<WorkforceEmployment> Employments { get; set; } = new List<WorkforceEmployment>();
		public WorkforceEmployment EditingEmployment { get; set; }
		public WorkforceJobAssignment EditingAssignment { get; set; }
		public List<SelectListItem> Establishments { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Contractors { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Affiliates { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();
		public IReadOnlyList<CaPayDataCode> JobCategories { get; set; } = CaPayDataSchemaProfile.Current.JobCategories;
		public string ProfileCode { get; set; } = CaPayDataSchemaProfile.Current.Code;
	}

	public class WorkforceCompensationView : WorkforcePageView
	{
		public List<EmployeeCompensationProfile> Defaults { get; set; } = new List<EmployeeCompensationProfile>();
		public List<EmployeeCompensationProfile> Employee { get; set; } = new List<EmployeeCompensationProfile>();
		public string EmploymentId { get; set; }
		public string WorkerName { get; set; }
		public Dictionary<int, string> RoleNames { get; set; } = new Dictionary<int, string>();
	}

	public class WorkforceCompensationProfileView : WorkforcePageView
	{
		public EmployeeCompensationProfile Profile { get; set; }
		public string PayComponentsJson { get; set; }
		public string CostComponentsJson { get; set; }
		public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();
		public string WorkerName { get; set; }
		public LaborCostResult Preview { get; set; }
	}

	public class WorkforceAnnualFactsView : WorkforcePageView
	{
		public int ReportingYear { get; set; }
		public PayDataReportTypes ReportType { get; set; }
		public List<WorkforceAnnualPayFact> Facts { get; set; } = new List<WorkforceAnnualPayFact>();
		public Dictionary<string, string> EmploymentLabels { get; set; } = new Dictionary<string, string>();
		public WorkforceImportResult ImportResult { get; set; }
		public string Csv { get; set; }
		public WorkforceAnnualPayFact Editing { get; set; }
		public List<SelectListItem> Employments { get; set; } = new List<SelectListItem>();
	}

	public class WorkforceWorkEntriesView : WorkforcePageView
	{
		public DateTime From { get; set; }
		public DateTime To { get; set; }
		public List<WorkforceWorkEntry> Entries { get; set; } = new List<WorkforceWorkEntry>();
		public Dictionary<string, string> WorkerNames { get; set; } = new Dictionary<string, string>();
		public WorkforceWorkEntry Editing { get; set; }
		public List<SelectListItem> Workers { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Establishments { get; set; } = new List<SelectListItem>();
	}

	public class WorkforceResourceCostsView : WorkforcePageView
	{
		public List<ResourceCostProfile> Profiles { get; set; } = new List<ResourceCostProfile>();
		public ResourceCostProfile Editing { get; set; }
		public string ComponentsJson { get; set; }
		public List<SelectListItem> Units { get; set; } = new List<SelectListItem>();
	}

	public class WorkforceUsageView : WorkforcePageView
	{
		public string DeploymentId { get; set; }
		public int? CallId { get; set; }
		public string ContextLabel { get; set; }
		public List<ResourceUsageEntry> Entries { get; set; } = new List<ResourceUsageEntry>();
		public ResourceUsageEntry Editing { get; set; }
		public List<SelectListItem> Units { get; set; } = new List<SelectListItem>();
		public Dictionary<int, string> UnitNames { get; set; } = new Dictionary<int, string>();
	}

	public class WorkforceCostRunsView : WorkforcePageView
	{
		public List<FieldCostRun> Runs { get; set; } = new List<FieldCostRun>();
		public List<SelectListItem> Deployments { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Bids { get; set; } = new List<SelectListItem>();
		public Dictionary<string, string> ContextLabels { get; set; } = new Dictionary<string, string>();
	}

	public class WorkforceCostRunView : WorkforcePageView
	{
		public FieldCostRun Run { get; set; }
		public FieldCostSummary Summary { get; set; }
		public string ContextLabel { get; set; }
		public FieldCostComparison Comparison { get; set; }
	}

	public class WorkforcePayDataView : WorkforcePageView
	{
		public int ReportingYear { get; set; }
		public List<PayDataReportRun> Runs { get; set; } = new List<PayDataReportRun>();
		public PayDataReadiness Readiness { get; set; }
		public DemographicCompleteness Completeness { get; set; }
		public IReadOnlyList<CaPayDataSchemaProfile> Profiles { get; set; } = CaPayDataSchemaProfile.All;
	}

	public class WorkforcePayDataRunView : WorkforcePageView
	{
		public PayDataReportRun Run { get; set; }
		public CaPayDataSchemaProfile Profile { get; set; }
		public List<PayDataReportEmployeeSnapshot> Snapshots { get; set; } = new List<PayDataReportEmployeeSnapshot>();
		public List<PayDataReportRow> Rows { get; set; } = new List<PayDataReportRow>();
		public List<PayDataExportArtifact> Artifacts { get; set; } = new List<PayDataExportArtifact>();
		public PayDataValidationResult Validation { get; set; }
		public Dictionary<string, string> EstablishmentLabels { get; set; } = new Dictionary<string, string>();
		public string Tab { get; set; } = "snapshots";
	}

	public class WorkforceWorksheetView : WorkforcePageView
	{
		public PayDataPortalWorksheet Worksheet { get; set; }
	}

	public class WorkforceDemographicsView : WorkforcePageView
	{
		public PayDataReportingDemographic Response { get; set; }
		public bool IsOwn { get; set; } = true;
		public string WorkerId { get; set; }
		public string WorkerName { get; set; }
		public string Reason { get; set; }
		public IReadOnlyList<CaPayDataCode> Races { get; set; } = CaPayDataSchemaProfile.Current.RaceEthnicities;
		public IReadOnlyList<CaPayDataCode> Sexes { get; set; } = CaPayDataSchemaProfile.Current.Sexes;
	}

	public class WorkforceDemographicInput
	{
		public string HispanicLatino { get; set; }
		public string[] RaceCodes { get; set; }
		public string SexCode { get; set; }
		public bool DeclinedRaceEthnicity { get; set; }
		public bool DeclinedSex { get; set; }
		public int CollectionSource { get; set; }
		public string Reason { get; set; }
	}
}
