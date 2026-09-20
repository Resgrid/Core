using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using Newtonsoft.Json;

namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (M0221): compensation profiles, pay / employer-cost components,
	// work entries and annual pay facts. Monetary values are ADP catalog 28 text columns (the envelope needs a text
	// column); the typed accessors parse them once decrypted. Hours, dates and codes are routing metadata.

	public static class ProtectedDecimal
	{
		public static decimal? Parse(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
		public static string Format(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture);
	}

	/// <summary>An employee's (or a role / department default) effective-dated compensation profile.</summary>
	public class EmployeeCompensationProfile : IEntity
	{
		[Required]
		public string EmployeeCompensationProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="CompensationScopes"/>.</summary>
		public int Scope { get; set; }
		public string WorkforceEmploymentId { get; set; }
		public int? PersonnelRoleId { get; set; }
		public DateTime EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Currency { get; set; } = "USD";
		/// <summary><see cref="PayBases"/>.</summary>
		public int PayBasis { get; set; }
		/// <summary>ADP catalog 28: base amount for the pay basis.</summary>
		public string BaseAmount { get; set; }
		/// <summary>ADP catalog 28: regular hourly equivalent.</summary>
		public string RegularHourlyEquivalent { get; set; }
		public decimal? StandardHoursPerDay { get; set; }
		public decimal? StandardHoursPerWeek { get; set; }
		public decimal? StandardHoursPerYear { get; set; }
		/// <summary>ADP catalog 28: multipliers per pay code as JSON ({"Overtime":1.5,"DoubleTime":2,"Standby":0.5,"Travel":1}).</summary>
		public string RateMultipliersJson { get; set; }
		public string Source { get; set; }
		public string ImportBatchId { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsApproved { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? BaseAmountValue { get => ProtectedDecimal.Parse(BaseAmount); set => BaseAmount = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? RegularHourlyEquivalentValue { get => ProtectedDecimal.Parse(RegularHourlyEquivalent); set => RegularHourlyEquivalent = ProtectedDecimal.Format(value); }
		[NotMapped] public List<EmployeePayComponent> PayComponents { get; set; } = new List<EmployeePayComponent>();
		[NotMapped] public List<EmployeeCostComponent> CostComponents { get; set; } = new List<EmployeeCostComponent>();
		public bool Covers(DateTime asOf) => EffectiveOn.Date <= asOf.Date && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "EmployeeCompensationProfiles";
		[NotMapped] public string IdName => "EmployeeCompensationProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => EmployeeCompensationProfileId; set => EmployeeCompensationProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "BaseAmountValue", "RegularHourlyEquivalentValue", "PayComponents", "CostComponents" };
	}

	/// <summary>A pay component (specialty, incentive, longevity …) on a compensation profile.</summary>
	public class EmployeePayComponent : IEntity
	{
		[Required]
		public string EmployeePayComponentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string EmployeeCompensationProfileId { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary><see cref="PayComponentCategories"/>.</summary>
		public int Category { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="PayComponentBases"/>.</summary>
		public int Basis { get; set; }
		/// <summary>ADP catalog 28: amount or percent.</summary>
		public string Amount { get; set; }
		/// <summary>Comma-separated <see cref="PayCodes"/> names the component applies to (blank = all).</summary>
		public string EligiblePayCodesCsv { get; set; }
		/// <summary>True when the component is paid with every overtime hour (the CFAA Salary Survey includes it; fixed-period incentives are excluded).</summary>
		public bool PaidForEachOvertimeHour { get; set; }
		public string SourceAgreement { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? AmountValue { get => ProtectedDecimal.Parse(Amount); set => Amount = ProtectedDecimal.Format(value); }
		public bool Covers(DateTime asOf) => (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= asOf.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "EmployeePayComponents";
		[NotMapped] public string IdName => "EmployeePayComponentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => EmployeePayComponentId; set => EmployeePayComponentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "AmountValue" };
	}

	/// <summary>An employer-cost component (payroll tax, workers' comp, pension, benefits, overhead) on a profile.</summary>
	public class EmployeeCostComponent : IEntity
	{
		[Required]
		public string EmployeeCostComponentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string EmployeeCompensationProfileId { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary><see cref="CostComponentCategories"/>.</summary>
		public int Category { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="CostComponentBases"/>.</summary>
		public int Basis { get; set; }
		/// <summary>ADP catalog 28: rate (percent) or amount.</summary>
		public string RateAmount { get; set; }
		public string EligiblePayCodesCsv { get; set; }
		/// <summary>ADP catalog 28: optional annual cap.</summary>
		public string Cap { get; set; }
		public string Source { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? RateAmountValue { get => ProtectedDecimal.Parse(RateAmount); set => RateAmount = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? CapValue { get => ProtectedDecimal.Parse(Cap); set => Cap = ProtectedDecimal.Format(value); }
		public bool Covers(DateTime asOf) => (!EffectiveOn.HasValue || EffectiveOn.Value.Date <= asOf.Date) && (!ExpiresOn.HasValue || ExpiresOn.Value.Date >= asOf.Date);

		[NotMapped] public string TableName => "EmployeeCostComponents";
		[NotMapped] public string IdName => "EmployeeCostComponentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => EmployeeCostComponentId; set => EmployeeCostComponentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "RateAmountValue", "CapValue" };
	}

	/// <summary>An imported / field work fact (hours by type, place, source reference) — a fact store, not a punch clock.</summary>
	public class WorkforceWorkEntry : IEntity
	{
		[Required]
		public string WorkforceWorkEntryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceWorkerId { get; set; }
		public string WorkforceEmploymentId { get; set; }
		public DateTime WorkDate { get; set; }
		public DateTime? StartTime { get; set; }
		public DateTime? EndTime { get; set; }
		public decimal Hours { get; set; }
		/// <summary><see cref="WorkHoursTypes"/>.</summary>
		public int HoursType { get; set; }
		public string WorkforceEstablishmentId { get; set; }
		public string WorkCountry { get; set; }
		public string WorkSubdivision { get; set; }
		/// <summary><see cref="WorkModes"/>.</summary>
		public int WorkMode { get; set; }
		public int? CallId { get; set; }
		public string DeploymentId { get; set; }
		public string DeploymentTimeReportId { get; set; }
		/// <summary>ADP catalog 28: the approved payroll cost of the entry when the payroll system supplied it.</summary>
		public string ApprovedPayrollCost { get; set; }
		public string ExternalSource { get; set; }
		public string ExternalId { get; set; }
		public string ImportBatchId { get; set; }
		public bool IsApproved { get; set; }
		public bool IsReconciled { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? ApprovedPayrollCostValue { get => ProtectedDecimal.Parse(ApprovedPayrollCost); set => ApprovedPayrollCost = ProtectedDecimal.Format(value); }

		[NotMapped] public string TableName => "WorkforceWorkEntries";
		[NotMapped] public string IdName => "WorkforceWorkEntryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceWorkEntryId; set => WorkforceWorkEntryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "ApprovedPayrollCostValue" };
	}

	/// <summary>The annual W-2 / client-allocated facts CRD reporting needs (unique per employment, year and client allocation; corrections version).</summary>
	public class WorkforceAnnualPayFact : IEntity
	{
		[Required]
		public string WorkforceAnnualPayFactId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceEmploymentId { get; set; }
		public int ReportingYear { get; set; }
		/// <summary><see cref="PayDataReportTypes"/>.</summary>
		public int ReportType { get; set; }
		public string ClientAllocationKey { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string W2Box5 { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string W2Box1 { get; set; }
		/// <summary>ADP catalog 28: the earnings the report uses.</summary>
		public string EarningsUsed { get; set; }
		/// <summary><see cref="EarningsSources"/>.</summary>
		public int EarningsSource { get; set; }
		public decimal? ActualWorkedHours { get; set; }
		public decimal? PaidLeaveHours { get; set; }
		public decimal? ReportableHours { get; set; }
		public int? DaysWorked { get; set; }
		public decimal? WeeksWorked { get; set; }
		/// <summary><see cref="ExemptProxyMethods"/>.</summary>
		public int ExemptProxyMethod { get; set; }
		public decimal? ProxyAverageHoursPerDay { get; set; }
		/// <summary>ADP catalog 28: contractor earnings allocated to this client.</summary>
		public string ClientAllocatedEarnings { get; set; }
		public decimal? ClientAllocatedHours { get; set; }
		public decimal? ClientAllocatedWeeks { get; set; }
		public string Source { get; set; }
		public string ImportBatchId { get; set; }
		public string SourceChecksum { get; set; }
		public bool IsReconciled { get; set; }
		public bool IsApproved { get; set; }
		public int Version { get; set; } = 1;
		public string SupersedesFactId { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? W2Box5Value { get => ProtectedDecimal.Parse(W2Box5); set => W2Box5 = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? W2Box1Value { get => ProtectedDecimal.Parse(W2Box1); set => W2Box1 = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? EarningsUsedValue { get => ProtectedDecimal.Parse(EarningsUsed); set => EarningsUsed = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? ClientAllocatedEarningsValue { get => ProtectedDecimal.Parse(ClientAllocatedEarnings); set => ClientAllocatedEarnings = ProtectedDecimal.Format(value); }

		[NotMapped] public string TableName => "WorkforceAnnualPayFacts";
		[NotMapped] public string IdName => "WorkforceAnnualPayFactId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => WorkforceAnnualPayFactId; set => WorkforceAnnualPayFactId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "W2Box5Value", "W2Box1Value", "EarningsUsedValue", "ClientAllocatedEarningsValue" };
	}
}
