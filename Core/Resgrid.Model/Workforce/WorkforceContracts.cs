using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (E3): the pure calculators' inputs and outputs, the report run's
	// validation / exception contracts, import results and the aggregate cost summary a mobile client may see.

	#region Labor cost

	/// <summary>One approved work quantity to price for one employment.</summary>
	public sealed class LaborWorkQuantity
	{
		public string WorkforceEmploymentId { get; set; }
		public string SubjectLabel { get; set; }
		public DateTime? WorkDate { get; set; }
		/// <summary><see cref="PayCodes"/>.</summary>
		public int PayCode { get; set; }
		public decimal Hours { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
		/// <summary>Approved payroll cost supplied by the payroll system (actual runs); when present it replaces the estimate.</summary>
		public decimal? ApprovedPayrollCost { get; set; }
	}

	public sealed class LaborCostInput
	{
		public LaborWorkQuantity Work { get; set; }
		/// <summary>The effective profile (employee, role default or department default) — null when none resolves.</summary>
		public EmployeeCompensationProfile Profile { get; set; }
		public bool IsFallback { get; set; }
		public DateTime AsOf { get; set; }
		public string Currency { get; set; } = "USD";
	}

	public sealed class LaborCostResult
	{
		public decimal BaseRate { get; set; }
		public decimal Multiplier { get; set; } = 1m;
		public decimal PayAmount { get; set; }
		public decimal PayComponentAmount { get; set; }
		public decimal EmployerCostAmount { get; set; }
		public decimal LoadedCost => Math.Round(PayAmount + PayComponentAmount + EmployerCostAmount, 2, MidpointRounding.AwayFromZero);
		public bool NeedsReview { get; set; }
		public bool IsFallback { get; set; }
		public bool IsEstimated { get; set; } = true;
		public List<string> ReviewReasons { get; set; } = new List<string>();
		public List<LaborCostDetail> Details { get; set; } = new List<LaborCostDetail>();
	}

	public sealed class LaborCostDetail
	{
		public string Kind { get; set; }
		public string Name { get; set; }
		public string Basis { get; set; }
		public decimal Rate { get; set; }
		public decimal Amount { get; set; }
		public string ComponentId { get; set; }
		public int? Version { get; set; }
	}

	public static class LaborReviewReasons
	{
		public const string NoProfile = "no_compensation_profile";
		public const string RoleFallback = "role_default_fallback";
		public const string DepartmentFallback = "department_default_fallback";
		public const string CurrencyMismatch = "currency_mismatch";
		public const string UnapprovedProfile = "unapproved_profile";
		public const string NoRate = "no_rate_for_basis";
		public const string PayCodeMultiplierMissing = "pay_code_multiplier_missing";
		public const string ApprovedPayrollCostUsed = "approved_payroll_cost_used";
	}

	#endregion

	#region Resource cost

	public sealed class ResourceUsageQuantity
	{
		public string SubjectLabel { get; set; }
		public DateTime? UsageDate { get; set; }
		public decimal Miles { get; set; }
		public decimal EngineHours { get; set; }
		public decimal OperatingHours { get; set; }
		public decimal IdleHours { get; set; }
		public decimal Days { get; set; }
		public decimal Deployments { get; set; }
		public decimal? ActualFuelCost { get; set; }
		public string SourceType { get; set; }
		public string SourceId { get; set; }
	}

	public sealed class ResourceCostInput
	{
		public ResourceUsageQuantity Usage { get; set; }
		public ResourceCostProfile Profile { get; set; }
		public bool IsFallback { get; set; }
		public DateTime AsOf { get; set; }
		/// <summary>Component ids of repairs already charged as direct lines (double-count prevention for rolling maintenance).</summary>
		public HashSet<string> ExcludedComponentIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public sealed class ResourceCostResult
	{
		public List<ResourceCostDetail> Details { get; set; } = new List<ResourceCostDetail>();
		public decimal Total => Math.Round(Details.Sum(d => d.Amount), 2, MidpointRounding.AwayFromZero);
		public bool NeedsReview { get; set; }
		public bool IsFallback { get; set; }
		public List<string> ReviewReasons { get; set; } = new List<string>();
	}

	public sealed class ResourceCostDetail
	{
		/// <summary><see cref="ResourceCostCategories"/> name.</summary>
		public string Category { get; set; }
		public string Basis { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		public decimal Rate { get; set; }
		public decimal Amount { get; set; }
		public string ComponentId { get; set; }
		public int? Version { get; set; }
		public bool Blocked { get; set; }
		public string Reason { get; set; }
	}

	public static class ResourceReviewReasons
	{
		public const string NoProfile = "no_resource_profile";
		public const string ClassFallback = "class_default_fallback";
		public const string DepreciationInputsMissing = "depreciation_inputs_missing";
		public const string FuelInputsMissing = "fuel_inputs_missing";
		public const string UtilizationMissing = "utilization_missing";
		public const string RollingWindowInsufficient = "rolling_window_insufficient";
		public const string RepairDoubleCounted = "repair_double_counted";
		public const string ComponentUnapproved = "component_unapproved";
	}

	#endregion

	#region Field cost runs

	public sealed class FieldCostSummary
	{
		public string FieldCostRunId { get; set; }
		public int ContextType { get; set; }
		public int RunType { get; set; }
		public int Status { get; set; }
		public DateTime? ThroughDate { get; set; }
		public string Currency { get; set; }
		public decimal PersonnelTotal { get; set; }
		public decimal ResourceTotal { get; set; }
		public decimal ConsumableTotal { get; set; }
		public decimal ExpenseTotal { get; set; }
		public decimal OverheadTotal { get; set; }
		public decimal TotalLoadedCost { get; set; }
		public int RevenueSource { get; set; }
		public decimal? RevenueAmount { get; set; }
		public decimal? ContributionMargin { get; set; }
		public decimal? ContributionMarginPercent { get; set; }
		public decimal BreakEvenRevenue { get; set; }
		public int MissingInputCount { get; set; }
		public DateTime? FrozenOn { get; set; }
	}

	public sealed class FieldCostComparison
	{
		public FieldCostRun Estimate { get; set; }
		public FieldCostRun Actual { get; set; }
		public decimal PersonnelVariance => (Actual?.PersonnelTotal ?? 0) - (Estimate?.PersonnelTotal ?? 0);
		public decimal ResourceVariance => (Actual?.ResourceTotal ?? 0) - (Estimate?.ResourceTotal ?? 0);
		public decimal ExpenseVariance => (Actual?.ExpenseTotal ?? 0) - (Estimate?.ExpenseTotal ?? 0);
		public decimal TotalVariance => (Actual?.TotalLoadedCost ?? 0) - (Estimate?.TotalLoadedCost ?? 0);
		public decimal? RevenueVariance => Actual?.RevenueAmount.HasValue == true && Estimate?.RevenueAmount.HasValue == true ? Actual.RevenueAmount - Estimate.RevenueAmount : null;
	}

	/// <summary>What a bid estimate prices: assumed hours per employment / role and assumed resource usage.</summary>
	public sealed class BidCostAssumptions
	{
		public List<LaborWorkQuantity> Labor { get; set; } = new List<LaborWorkQuantity>();
		public List<(int? PersonnelRoleId, int PayCode, decimal Hours, string Label)> RoleLabor { get; set; } = new List<(int?, int, decimal, string)>();
		public List<(int SubjectType, int? UnitId, string AssetId, string ExternalKey, ResourceUsageQuantity Usage)> Resources { get; set; } = new List<(int, int?, string, string, ResourceUsageQuantity)>();
		public decimal ExpenseAllowance { get; set; }
	}

	#endregion

	#region Pay data reporting

	public sealed class PayDataValidationIssue
	{
		public string Code { get; set; }
		public string Scope { get; set; }
		public string SubjectId { get; set; }
		public string Detail { get; set; }
		public bool IsBlocking { get; set; }
	}

	public sealed class PayDataValidationResult
	{
		public string RunId { get; set; }
		public DateTime ValidatedOn { get; set; }
		public List<PayDataValidationIssue> Errors { get; set; } = new List<PayDataValidationIssue>();
		public List<PayDataValidationIssue> Warnings { get; set; } = new List<PayDataValidationIssue>();
		public bool CanFreeze => Errors.Count == 0;
	}

	public static class PayDataValidationCodes
	{
		public const string ProfileStale = "profile_stale";
		public const string SnapshotOutsideWindow = "snapshot_outside_window";
		public const string EmployerIdentityMissing = "employer_identity_missing";
		public const string CoverageUndeclared = "coverage_undeclared";
		public const string NoEmployees = "no_employees";
		public const string EstablishmentMissing = "establishment_missing";
		public const string EstablishmentNaicsMissing = "establishment_naics_missing";
		public const string EstablishmentAddressMissing = "establishment_address_missing";
		public const string ContractorIdentityMissing = "contractor_identity_missing";
		public const string JobCategoryMissing = "job_category_missing";
		public const string JobCategoryUnknown = "job_category_unknown";
		public const string DemographicMissing = "demographic_missing";
		public const string AnnualFactMissing = "annual_fact_missing";
		public const string AnnualFactUnapproved = "annual_fact_unapproved";
		public const string EarningsMissing = "earnings_missing";
		public const string EarningsBox1Fallback = "earnings_box1_fallback";
		public const string HoursZero = "hours_zero";
		public const string WeeksMissing = "weeks_missing";
		public const string ExemptProxyUsed = "exempt_proxy_used";
		public const string AssignmentOverlap = "assignment_overlap";
		public const string RemoteCountsMismatch = "remote_counts_mismatch";
		public const string EmployeeCountMismatch = "employee_count_mismatch";
		public const string FieldTooLong = "field_too_long";
		public const string FileTooLarge = "file_too_large";
		public const string ObserverPerceptionUsed = "observer_perception_used";
		public const string ManualOverride = "manual_override";
		public const string WorkModeUnresolved = "work_mode_unresolved";
	}

	/// <summary>The portal-entry worksheet (employer totals, establishments, affiliates, snapshot dates, filing contact) — protected, no-store.</summary>
	public sealed class PayDataPortalWorksheet
	{
		public string RunId { get; set; }
		public string ProfileCode { get; set; }
		public int ReportType { get; set; }
		public int ReportingYear { get; set; }
		public DateTime SnapshotStart { get; set; }
		public DateTime SnapshotEnd { get; set; }
		public string EmployerLegalName { get; set; }
		public string EmployerFein { get; set; }
		public string EmployerSein { get; set; }
		public string EmployerSosNumber { get; set; }
		public string EmployerNaics { get; set; }
		public string EddAddress { get; set; }
		public string HeadquartersAddress { get; set; }
		public bool IsIntegratedEnterprise { get; set; }
		public string FilingContactName { get; set; }
		public string FilingContactEmail { get; set; }
		public string FilingContactPhone { get; set; }
		public int? UsEmployeeCount { get; set; }
		public int? CaliforniaEmployeeCount { get; set; }
		public int SnapshotEmployeeCount { get; set; }
		public List<PayDataWorksheetEstablishment> Establishments { get; set; } = new List<PayDataWorksheetEstablishment>();
		public List<PayDataWorksheetAffiliate> Affiliates { get; set; } = new List<PayDataWorksheetAffiliate>();
		public string RunRemarks { get; set; }
		public DateTime DueDate { get; set; }
	}

	public sealed class PayDataWorksheetEstablishment
	{
		public string Code { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string Zip { get; set; }
		public string Naics { get; set; }
		public string MajorActivity { get; set; }
		public bool IsHeadquarters { get; set; }
		public bool? WasFiledPriorYear { get; set; }
		public int EmployeeCount { get; set; }
	}

	public sealed class PayDataWorksheetAffiliate
	{
		public string LegalName { get; set; }
		public string Fein { get; set; }
		public string Sein { get; set; }
		public string SosNumber { get; set; }
		public string HeadquartersAddress { get; set; }
	}

	/// <summary>Completeness view for the compliance officer: counts only, never values.</summary>
	public sealed class DemographicCompleteness
	{
		public int ActiveWorkers { get; set; }
		public int WithResponse { get; set; }
		public int SelfIdentified { get; set; }
		public int Declined { get; set; }
		public int ObserverPerception { get; set; }
		public int Missing => Math.Max(0, ActiveWorkers - WithResponse);
	}

	public sealed class PayDataReadiness
	{
		public int ReportingYear { get; set; }
		public string ProfileCode { get; set; }
		public bool ProfileAvailable { get; set; }
		public DateTime DueDate { get; set; }
		public int CoverageStatus { get; set; }
		public int OpenRuns { get; set; }
		public int UnresolvedExceptions { get; set; }
		public int DemographicsMissing { get; set; }
		public int AnnualFactsMissing { get; set; }
		public bool HasFrozenRun { get; set; }
		public bool HasCertifiedRun { get; set; }
	}

	#endregion

	#region Imports

	public sealed class WorkforceImportResult
	{
		public bool DryRun { get; set; }
		public int Total { get; set; }
		public int Created { get; set; }
		public int Updated { get; set; }
		public int Skipped { get; set; }
		public string ImportBatchId { get; set; }
		public List<WorkforceImportIssue> Issues { get; set; } = new List<WorkforceImportIssue>();
		public bool HasErrors => Issues.Any(i => i.IsError);
	}

	public sealed class WorkforceImportIssue
	{
		public int Line { get; set; }
		public string Code { get; set; }
		public string Detail { get; set; }
		public bool IsError { get; set; }
	}

	#endregion
}
