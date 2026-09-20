using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Workforce
{
	// Workforce & Business Operations plan, Phase E (M0223): California §12999 pay-data reporting. Demographics live
	// in their own table / repository / service (never joined by personnel or compensation queries); runs, employee
	// snapshots, aggregate rows and export artifacts are immutable once frozen. Values are ADP catalog 28.

	/// <summary>A worker's separately collected demographic responses (voluntary self-identification first).</summary>
	public class PayDataReportingDemographic : IEntity
	{
		[Required]
		public string PayDataReportingDemographicId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceWorkerId { get; set; }
		public DateTime EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>ADP catalog 28: "Yes" / "No" / "Declined".</summary>
		public string HispanicLatino { get; set; }
		/// <summary>ADP catalog 28: comma-separated stable race / ethnicity codes (see <see cref="CaPayDataSchemaProfile"/>).</summary>
		public string RaceEthnicityCodes { get; set; }
		/// <summary>ADP catalog 28: sex reporting code (F / M / N / Declined).</summary>
		public string SexCode { get; set; }
		public bool DeclinedRaceEthnicity { get; set; }
		public bool DeclinedSex { get; set; }
		/// <summary><see cref="DemographicCollectionSources"/>.</summary>
		public int CollectionSource { get; set; }
		public DateTime? CollectedOn { get; set; }
		public string CollectedByUserId { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }
		public int Version { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public string TableName => "PayDataReportingDemographics";
		[NotMapped] public string IdName => "PayDataReportingDemographicId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PayDataReportingDemographicId; set => PayDataReportingDemographicId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One CRD report run (payroll or labor contractor) for a reporting year and snapshot period.</summary>
	public class PayDataReportRun : IEntity
	{
		[Required]
		public string PayDataReportRunId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="PayDataReportTypes"/>.</summary>
		public int ReportType { get; set; }
		public int ReportingYear { get; set; }
		public string SchemaProfileCode { get; set; }
		public string SchemaProfileHash { get; set; }
		public DateTime SnapshotStart { get; set; }
		public DateTime SnapshotEnd { get; set; }
		/// <summary>ADP catalog 28: the employer / affiliate identity as of the run.</summary>
		public string EmployerSnapshotJson { get; set; }
		public DateTime? SourceCutoff { get; set; }
		/// <summary><see cref="PayDataReportRunStatuses"/>.</summary>
		public int Status { get; set; }
		public int EmployeeCount { get; set; }
		public int RowCount { get; set; }
		public int ExceptionCount { get; set; }
		public int WarningCount { get; set; }
		public string ValidationSummaryJson { get; set; }
		/// <summary>ADP catalog 28: run-level clarifying remarks.</summary>
		public string RunRemarks { get; set; }
		public string SupersedesRunId { get; set; }
		public string ReviewedByUserId { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public string FrozenByUserId { get; set; }
		public DateTime? FrozenOn { get; set; }
		public string ExportedByUserId { get; set; }
		public DateTime? ExportedOn { get; set; }
		public string CertifiedByUserId { get; set; }
		public DateTime? CertifiedOn { get; set; }
		public string CertificationReference { get; set; }
		public string CertifiedArtifactChecksum { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public bool IsFrozen => Status >= (int)PayDataReportRunStatuses.FrozenForExport && Status != (int)PayDataReportRunStatuses.Void;
		[NotMapped] public bool IsEditable => Status is (int)PayDataReportRunStatuses.Draft or (int)PayDataReportRunStatuses.Validated;

		[NotMapped] public string TableName => "PayDataReportRuns";
		[NotMapped] public string IdName => "PayDataReportRunId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PayDataReportRunId; set => PayDataReportRunId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsFrozen", "IsEditable" };
	}

	/// <summary>One employee's resolved facts for a run (immutable after freeze).</summary>
	public class PayDataReportEmployeeSnapshot : IEntity
	{
		[Required]
		public string PayDataReportEmployeeSnapshotId { get; set; }
		[Required]
		public string PayDataReportRunId { get; set; }
		public int DepartmentId { get; set; }
		[Required]
		public string WorkforceWorkerId { get; set; }
		public string WorkforceEmploymentId { get; set; }
		public string WorkforceEstablishmentId { get; set; }
		public string WorkforceLaborContractorId { get; set; }
		public string JobCategoryCode { get; set; }
		/// <summary>ADP catalog 28: the derived combined race/ethnicity/sex upload code.</summary>
		public string DemographicCode { get; set; }
		public string PayBandCode { get; set; }
		public string ExemptionCode { get; set; }
		public string EmploymentTypeCode { get; set; }
		/// <summary><see cref="WorkModes"/>.</summary>
		public int WorkMode { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string AnnualEarnings { get; set; }
		/// <summary><see cref="EarningsSources"/>.</summary>
		public int EarningsSource { get; set; }
		public decimal AnnualHours { get; set; }
		public decimal AnnualWeeks { get; set; }
		/// <summary>ADP catalog 28: annual earnings ÷ reportable hours.</summary>
		public string HourlyRate { get; set; }
		public bool IsIncluded { get; set; } = true;
		public string ExceptionCodesCsv { get; set; }
		public string OverrideReason { get; set; }
		public string OverrideByUserId { get; set; }
		public string SourceVersions { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? AnnualEarningsValue { get => ProtectedDecimal.Parse(AnnualEarnings); set => AnnualEarnings = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? HourlyRateValue { get => ProtectedDecimal.Parse(HourlyRate); set => HourlyRate = ProtectedDecimal.Format(value); }
		[NotMapped] public string WorkerDisplayName { get; set; }
		[NotMapped] public List<string> ExceptionCodes => string.IsNullOrWhiteSpace(ExceptionCodesCsv) ? new List<string>() : new List<string>(ExceptionCodesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries));

		[NotMapped] public string TableName => "PayDataReportEmployeeSnapshots";
		[NotMapped] public string IdName => "PayDataReportEmployeeSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PayDataReportEmployeeSnapshotId; set => PayDataReportEmployeeSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "AnnualEarningsValue", "HourlyRateValue", "WorkerDisplayName", "ExceptionCodes" };
	}

	/// <summary>One aggregated upload row (establishment × job category × demographic × pay band …).</summary>
	public class PayDataReportRow : IEntity
	{
		[Required]
		public string PayDataReportRowId { get; set; }
		[Required]
		public string PayDataReportRunId { get; set; }
		public int DepartmentId { get; set; }
		public string WorkforceEstablishmentId { get; set; }
		public string WorkforceLaborContractorId { get; set; }
		public string JobCategoryCode { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string DemographicCode { get; set; }
		public string PayBandCode { get; set; }
		public string ExemptionCode { get; set; }
		public string EmploymentTypeCode { get; set; }
		public int EmployeeCount { get; set; }
		public decimal AnnualHours { get; set; }
		public decimal AnnualWeeks { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string MeanHourlyRate { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string MedianHourlyRate { get; set; }
		public int NonRemoteCount { get; set; }
		public int RemoteWithinCaliforniaCount { get; set; }
		public int RemoteOutsideCaliforniaCount { get; set; }
		/// <summary>ADP catalog 28.</summary>
		public string RowRemarks { get; set; }
		public string ContributingSnapshotIdsCsv { get; set; }
		public int SortOrder { get; set; }
		public DateTime AddedOn { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public decimal? MeanHourlyRateValue { get => ProtectedDecimal.Parse(MeanHourlyRate); set => MeanHourlyRate = ProtectedDecimal.Format(value); }
		[NotMapped] public decimal? MedianHourlyRateValue { get => ProtectedDecimal.Parse(MedianHourlyRate); set => MedianHourlyRate = ProtectedDecimal.Format(value); }

		[NotMapped] public string TableName => "PayDataReportRows";
		[NotMapped] public string IdName => "PayDataReportRowId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PayDataReportRowId; set => PayDataReportRowId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "MeanHourlyRateValue", "MedianHourlyRateValue" };
	}

	/// <summary>A short-lived encrypted export (CSV / XLSX) of a frozen run; purged after the configured window.</summary>
	public class PayDataExportArtifact : IEntity
	{
		[Required]
		public string PayDataExportArtifactId { get; set; }
		[Required]
		public string PayDataReportRunId { get; set; }
		public int DepartmentId { get; set; }
		public string SchemaProfileCode { get; set; }
		public string SchemaProfileHash { get; set; }
		/// <summary><see cref="PayDataExportFormats"/>.</summary>
		public int Format { get; set; }
		public string FileName { get; set; }
		public string Checksum { get; set; }
		/// <summary>ADP catalog 28 (binary).</summary>
		public byte[] Data { get; set; }
		public int Size { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? PurgedOn { get; set; }
		public string ExportedByUserId { get; set; }
		public int DownloadCount { get; set; }
		public DateTime? LastDownloadedOn { get; set; }
		public string LastDownloadedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped] public bool IsAvailable => !PurgedOn.HasValue && ExpiresOn > DateTime.UtcNow;

		[NotMapped] public string TableName => "PayDataExportArtifacts";
		[NotMapped] public string IdName => "PayDataExportArtifactId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => PayDataExportArtifactId; set => PayDataExportArtifactId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsAvailable" };
	}
}
