using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	// RMS-1B/1C v4 surfaces (plan section 5.4): definitions, typed values, saved reports, export templates, deployments,
	// and the protected reveal. Schema/rule/report/diff contracts are the Model types themselves; only envelopes,
	// inputs and withholding-aware value shapes live here.

	#region Typed values

	public class RecordValueInputData
	{
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		public string RowKey { get; set; }
		public int Ordinal { get; set; }
		public string Value { get; set; }
		public List<string> Values { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string UnitCode { get; set; }
		public string CurrencyCode { get; set; }
		public int? OffsetMinutes { get; set; }
	}

	public class RecordValueCellData
	{
		public string FieldKey { get; set; }
		public string Label { get; set; }
		public string Type { get; set; }
		public string Classification { get; set; }
		public string Display { get; set; }
		public string Value { get; set; }
		public List<string> Values { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string UnitCode { get; set; }
		public string CurrencyCode { get; set; }
		public int? OffsetMinutes { get; set; }
		public decimal? Number { get; set; }
		public decimal? CanonicalNumber { get; set; }
		public string CanonicalUnitCode { get; set; }
		public bool Withheld { get; set; }
	}

	public class RecordValueRowData
	{
		public string RowKey { get; set; }
		public int Ordinal { get; set; }
		public List<RecordValueCellData> Cells { get; set; } = new List<RecordValueCellData>();
	}

	public class RecordValueSectionData
	{
		public string SectionKey { get; set; }
		public string Label { get; set; }
		public bool Repeating { get; set; }
		public List<RecordValueRowData> Rows { get; set; } = new List<RecordValueRowData>();
	}

	public class RecordRowRulesData
	{
		public string SectionKey { get; set; }
		public string RowKey { get; set; }
		public List<string> HiddenFieldKeys { get; set; } = new List<string>();
		public List<string> RequiredFieldKeys { get; set; } = new List<string>();
	}

	public class RecordValuesData
	{
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public List<RecordValueSectionData> Sections { get; set; } = new List<RecordValueSectionData>();
		public List<string> WithheldFieldKeys { get; set; } = new List<string>();
		public List<string> HiddenSectionKeys { get; set; } = new List<string>();
		public List<string> HiddenFieldKeys { get; set; } = new List<string>();
		public List<string> RequiredFieldKeys { get; set; } = new List<string>();
		/// <summary>Per-row rule outcomes for repeating sections (a field rule may look at its own row).</summary>
		public List<RecordRowRulesData> RowRules { get; set; } = new List<RecordRowRulesData>();
	}

	#endregion

	#region Definitions

	public class RecordDefinitionsResult : StandardApiResponseV4Base
	{
		public List<RecordDefinitionSummary> Data { get; set; } = new List<RecordDefinitionSummary>();
	}

	public class RecordDefinitionVersionData
	{
		public string VersionId { get; set; }
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public string State { get; set; }
		public string LifecyclePreset { get; set; }
		public List<int> ReviewerRoleIds { get; set; } = new List<int>();
		public List<int> ApproverRoleIds { get; set; } = new List<int>();
		public int? ReviewDueHours { get; set; }
		public int? ApproveDueHours { get; set; }
		public bool RequireAuthorAttestation { get; set; }
		public RecordDefinitionNumbering Numbering { get; set; }
		public int? RetentionYears { get; set; }
		public string Classification { get; set; }
		public RecordDefinitionSchema Schema { get; set; }
		public string SchemaChecksum { get; set; }
		public string MinimumClientCapability { get; set; }
		public RecordDefinitionClientSurface ClientSurface { get; set; }
		public List<RecordDefinitionFieldMapping> MigrationMap { get; set; } = new List<RecordDefinitionFieldMapping>();
		public string ChangeNotes { get; set; }
		public DateTime? PublishedOn { get; set; }
		public string PublishedByUserId { get; set; }
		public DateTime? RetiredOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
	}

	public class RecordDefinitionDetailData
	{
		public string DefinitionId { get; set; }
		public string Key { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string Owner { get; set; }
		public string TemplateKey { get; set; }
		public string JurisdictionProfileKey { get; set; }
		public string PermittedSubjectTypes { get; set; }
		public int? CurrentPublishedVersion { get; set; }
		public int LatestVersion { get; set; }
		public bool IsRetired { get; set; }
		public DateTime? RetiredOn { get; set; }
		public string RetiredReason { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
		public List<RecordDefinitionVersionData> Versions { get; set; } = new List<RecordDefinitionVersionData>();
	}

	public class RecordDefinitionResult : StandardApiResponseV4Base
	{
		public RecordDefinitionDetailData Data { get; set; }
	}

	public class RecordDefinitionVersionResult : StandardApiResponseV4Base
	{
		public RecordDefinitionVersionData Data { get; set; }
	}

	public class RecordDefinitionVersionsResult : StandardApiResponseV4Base
	{
		public List<RecordDefinitionVersionData> Data { get; set; } = new List<RecordDefinitionVersionData>();
	}

	public class RecordDefinitionValidationResult : StandardApiResponseV4Base
	{
		public RecordDefinitionValidation Data { get; set; }
	}

	public class RecordDefinitionImpactResult : StandardApiResponseV4Base
	{
		public RecordDefinitionImpactPreview Data { get; set; }
	}

	public class RecordDefinitionDiffResult : StandardApiResponseV4Base
	{
		public RecordDefinitionDiff Data { get; set; }
	}

	/// <summary>The print layout for one definition version (RMS plan section 4.10.1).</summary>
	public class RecordDefinitionLayoutResult : StandardApiResponseV4Base
	{
		public RecordDefinitionLayoutData Data { get; set; }
	}

	public class RecordDefinitionLayoutData
	{
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		/// <summary>"{definition-key}/{n}" for the saved definition layout; null when none has been saved.</summary>
		public string StoredLayoutVersion { get; set; }
		/// <summary>The saved definition-scope configuration, or null.</summary>
		public RecordsDefinitionLayoutConfig Config { get; set; }
		/// <summary>True when the saved layout applies to the requested definition version.</summary>
		public bool AppliesToVersion { get; set; }
		/// <summary>The composite layout version the provenance footer stamps for this version.</summary>
		public string ResolvedLayoutVersion { get; set; }
		/// <summary>The branding block print resolves to (definition override or department default).</summary>
		public RecordsPrintLayoutConfig Branding { get; set; }
	}

	/// <summary>Bulk packet / bulk assign-for-review over an authorized selection (RMS plan section 4.7).</summary>
	public class RecordsBulkPacketInput
	{
		public List<string> RecordIds { get; set; } = new List<string>();
		/// <summary>1 = compiled PDF, 2 = zip bundle of per-record PDFs plus manifest.json.</summary>
		public int Mode { get; set; } = 1;
		public string Title { get; set; }
		public string Purpose { get; set; }
		public string DeliverToEmail { get; set; }
		public int? OriginClient { get; set; }
	}

	public class RecordsBulkAssignInput
	{
		public List<string> RecordIds { get; set; } = new List<string>();
		public string ReviewerUserId { get; set; }
		public string Reason { get; set; }
	}

	public class RecordsBulkResultApi : StandardApiResponseV4Base
	{
		public RecordsBulkData Data { get; set; }
	}

	public class RecordsBulkData
	{
		public int Processed { get; set; }
		public int Skipped { get; set; }
		public List<RecordsBulkSkip> Skips { get; set; } = new List<RecordsBulkSkip>();
		/// <summary>The stored packet run (download through RecordExportTemplates/DownloadRun); null for an assignment.</summary>
		public RecordExportRunData Run { get; set; }
		public bool Delivered { get; set; }
	}

	public class RecordDefinitionMigrationApiResult : StandardApiResponseV4Base
	{
		public RecordDefinitionMigrationResult Data { get; set; }
	}

	public class CreateRecordDefinitionInput
	{
		public string DefinitionKey { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string TemplateKey { get; set; }
		public string CloneFromDefinitionKey { get; set; }
		public string JurisdictionProfileKey { get; set; }
		public string Locale { get; set; }
	}

	public class SaveRecordDefinitionDraftInput
	{
		public long RowVersion { get; set; }
		public RecordDefinitionDraftInput Draft { get; set; }
	}

	public class PublishRecordDefinitionInput
	{
		public long RowVersion { get; set; }
	}

	public class RetireRecordDefinitionInput
	{
		public long RowVersion { get; set; }
		public string Reason { get; set; }
	}

	public class MigrateRecordDraftsInput
	{
		public int FromVersion { get; set; }
		public int ToVersion { get; set; }
		public List<RecordDefinitionFieldMapping> Mapping { get; set; } = new List<RecordDefinitionFieldMapping>();
		public bool Preview { get; set; } = true;
	}

	public class RecordTemplatePacksResult : StandardApiResponseV4Base
	{
		public List<RecordTemplatePackSummary> Data { get; set; } = new List<RecordTemplatePackSummary>();
	}

	public class RecordJurisdictionProfilesResult : StandardApiResponseV4Base
	{
		public List<RmsJurisdictionProfileVersion> Data { get; set; } = new List<RmsJurisdictionProfileVersion>();
	}

	public class RecordTemplateRenderingData
	{
		public string TemplateKey { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string PackKey { get; set; }
		public string LifecyclePreset { get; set; }
		public string NumberPrefix { get; set; }
		public string PermittedSubjectTypes { get; set; }
		public string ProfileKey { get; set; }
		public string Locale { get; set; }
		public string MeasurementSystem { get; set; }
		public string CurrencyCode { get; set; }
		public string ArtifactStatus { get; set; }
		public string ProvenanceStatement { get; set; }
		public List<RmsSourceProvenance> Sources { get; set; } = new List<RmsSourceProvenance>();
		public string MinimumClientCapability { get; set; }
		public RecordDefinitionSchema Schema { get; set; }
	}

	public class RecordTemplateRenderingResult : StandardApiResponseV4Base
	{
		public RecordTemplateRenderingData Data { get; set; }
	}

	#endregion

	#region Saved reports

	public class RecordSavedReportData
	{
		public string ReportId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public RecordReportSpec Spec { get; set; }
		public int MaxRowsPerRun { get; set; }
		public bool IncludeRestricted { get; set; }
		public DateTime? LastRunOn { get; set; }
		public string LastRunByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
	}

	public class RecordSavedReportsResult : StandardApiResponseV4Base
	{
		public List<RecordSavedReportData> Data { get; set; } = new List<RecordSavedReportData>();
	}

	public class RecordSavedReportResult : StandardApiResponseV4Base
	{
		public RecordSavedReportData Data { get; set; }
	}

	public class SaveRecordSavedReportInput
	{
		public string ReportId { get; set; }
		public long RowVersion { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public RecordReportSpec Spec { get; set; } = new RecordReportSpec();
		public int MaxRowsPerRun { get; set; } = RmsSavedReportDefinition.MaxRows;
		public bool IncludeRestricted { get; set; }
	}

	public class RecordReportValidationResult : StandardApiResponseV4Base
	{
		public RecordReportValidation Data { get; set; }
	}

	public class RecordReportRunResult : StandardApiResponseV4Base
	{
		public RecordReportResult Data { get; set; }
	}

	#endregion

	#region Export templates

	public class RecordExportTemplateData
	{
		public string TemplateId { get; set; }
		public string TemplateKey { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Format { get; set; }
		public string Scope { get; set; }
		public List<string> DefinitionKeys { get; set; } = new List<string>();
		public List<string> Columns { get; set; } = new List<string>();
		public bool IncludeNarrative { get; set; }
		public bool IncludeRestricted { get; set; }
		public DateTime? EgressAcknowledgedOn { get; set; }
		public string EgressAcknowledgedByUserId { get; set; }
		public string FileNameTemplate { get; set; }
		public bool IncludeHeader { get; set; }
		public string Delimiter { get; set; }
		public string ScheduleKind { get; set; }
		public int ScheduleHourLocal { get; set; }
		public int ScheduleDayOfWeek { get; set; }
		public int ScheduleDayOfMonth { get; set; }
		public int WindowDays { get; set; }
		public DateTime? NextRunOn { get; set; }
		public DateTime? LastRunOn { get; set; }
		public bool IsEnabled { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
	}

	public class RecordExportTemplatesResult : StandardApiResponseV4Base
	{
		public List<RecordExportTemplateData> Data { get; set; } = new List<RecordExportTemplateData>();
	}

	public class RecordExportTemplateResult : StandardApiResponseV4Base
	{
		public RecordExportTemplateData Data { get; set; }
		public List<string> Warnings { get; set; } = new List<string>();
	}

	public class SaveRecordExportTemplateInput
	{
		public string TemplateId { get; set; }
		public long RowVersion { get; set; }
		public string TemplateKey { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int Format { get; set; } = (int)RmsExportFormat.Csv;
		public int Scope { get; set; } = (int)RmsExportScope.TriggeringRecord;
		public List<string> DefinitionKeys { get; set; } = new List<string>();
		public List<string> Columns { get; set; } = new List<string>();
		public bool IncludeNarrative { get; set; }
		public bool IncludeRestricted { get; set; }
		public bool AcknowledgeEgress { get; set; }
		public string FileNameTemplate { get; set; }
		public bool IncludeHeader { get; set; } = true;
		public string Delimiter { get; set; } = ",";
		public int ScheduleKind { get; set; } = (int)RmsExportScheduleKind.None;
		public int ScheduleHourLocal { get; set; } = 6;
		public int ScheduleDayOfWeek { get; set; } = 1;
		public int ScheduleDayOfMonth { get; set; } = 1;
		public int WindowDays { get; set; }
		public bool IsEnabled { get; set; } = true;
	}

	public class RunRecordExportInput
	{
		public string RecordId { get; set; }
		public int? RecordKind { get; set; }
		public DateTime? WindowStart { get; set; }
		public DateTime? WindowEnd { get; set; }
	}

	public class RecordExportRunData
	{
		public string RunId { get; set; }
		public string TemplateId { get; set; }
		public string TemplateKey { get; set; }
		public string Trigger { get; set; }
		public string RecordId { get; set; }
		public DateTime? WindowStart { get; set; }
		public DateTime? WindowEnd { get; set; }
		public int RecordCount { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ByteSize { get; set; }
		public string Checksum { get; set; }
		public bool Redacted { get; set; }
		public DateTime GeneratedOn { get; set; }
		public string GeneratedByUserId { get; set; }
		public string WorkflowRunId { get; set; }
		public DateTime ExpiresOn { get; set; }
	}

	public class RecordExportRunsResult : StandardApiResponseV4Base
	{
		public List<RecordExportRunData> Data { get; set; } = new List<RecordExportRunData>();
	}

	public class RecordExportRunResult : StandardApiResponseV4Base
	{
		public RecordExportRunData Data { get; set; }
	}

	#endregion

	#region Deployments

	public class RecordDeploymentFillData
	{
		public string FillId { get; set; }
		public string RequestNumber { get; set; }
		public string ParentRequestNumber { get; set; }
		public string RequestCategory { get; set; }
		public string FillNumber { get; set; }
		public string ResourceKind { get; set; }
		public string ResourceType { get; set; }
		public string ResourceTypeScheme { get; set; }
		public string Position { get; set; }
		public string PositionScheme { get; set; }
		public bool IsTrainee { get; set; }
		public string HomeUnit { get; set; }
		public string HostAgency { get; set; }
		public string AgencyUnitId { get; set; }
		public string PointOfHire { get; set; }
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string AssignedUserId { get; set; }
		public int? AssignedUnitId { get; set; }
		public string Status { get; set; }
		public string DeclineReason { get; set; }
		public DateTime? RequestedOn { get; set; }
		public DateTime? NeededOn { get; set; }
		public DateTime? FilledOn { get; set; }
		public DateTime? MobilizedOn { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public DateTime? AssignedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? DemobilizedOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public string Notes { get; set; }
		public long RowVersion { get; set; }
	}

	public class RecordDeploymentData
	{
		public string OrderId { get; set; }
		public string RecordId { get; set; }
		public string RecordNumber { get; set; }
		public string RecordState { get; set; }
		public string ProfileKey { get; set; }
		public int ProfileVersion { get; set; }
		public string HomeProfileKey { get; set; }
		public string HostProfileKey { get; set; }
		public string SourceScheme { get; set; }
		public string SourceSystem { get; set; }
		public string OrderNumber { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string IncidentCountry { get; set; }
		public string IncidentSubdivision { get; set; }
		public string OrderingOffice { get; set; }
		public string DispatchOffice { get; set; }
		public string RequestingAgency { get; set; }
		public string ReceivingAgency { get; set; }
		public string SendingAgency { get; set; }
		public string DepartmentRole { get; set; }
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string CurrencyCode { get; set; }
		public string MeasurementSystem { get; set; }
		public string TimeZoneId { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
		public string SourceVersion { get; set; }
		public string ArtifactFileName { get; set; }
		public string ArtifactContentType { get; set; }
		public string ArtifactChecksum { get; set; }
		public bool HasArtifact { get; set; }
		public string ArtifactSafeUrl { get; set; }
		public string Status { get; set; }
		public DateTime? MobilizedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? ClosedOutOn { get; set; }
		public string CloseoutNotes { get; set; }
		public bool AllReturned { get; set; }
		public bool IsPreview { get; set; } = true;
		public string ProvenanceStatement { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
		public List<RecordDeploymentFillData> Fills { get; set; } = new List<RecordDeploymentFillData>();
	}

	public class RecordDeploymentsResult : StandardApiResponseV4Base
	{
		public List<RecordDeploymentData> Data { get; set; } = new List<RecordDeploymentData>();
	}

	public class RecordDeploymentResult : StandardApiResponseV4Base
	{
		public RecordDeploymentData Data { get; set; }
	}

	public class CreateRecordDeploymentInput
	{
		public string ProfileKey { get; set; }
		public string HomeProfileKey { get; set; }
		public string HostProfileKey { get; set; }
		public string SourceScheme { get; set; }
		public string SourceSystem { get; set; }
		public string OrderNumber { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string IncidentCountry { get; set; }
		public string IncidentSubdivision { get; set; }
		public string OrderingOffice { get; set; }
		public string DispatchOffice { get; set; }
		public string RequestingAgency { get; set; }
		public string ReceivingAgency { get; set; }
		public string SendingAgency { get; set; }
		public string DepartmentRole { get; set; } = "filling";
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string CurrencyCode { get; set; }
		public string MeasurementSystem { get; set; }
		public string TimeZoneId { get; set; }
		public int? CapturedOffsetMinutes { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
		public string SourceVersion { get; set; }
		public string ArtifactFileName { get; set; }
		public string ArtifactContentType { get; set; }
		/// <summary>Base64 of the order artifact (PDF, image, export); at most 25 MB decoded.</summary>
		public string ArtifactBase64 { get; set; }
		public string ArtifactSafeUrl { get; set; }
		public int? StationGroupId { get; set; }
		public string IdempotencyKey { get; set; }
		public List<RecordDeploymentFillInput> Fills { get; set; } = new List<RecordDeploymentFillInput>();
	}

	public class RecordDeploymentSnapshotInput
	{
		public string SourceVersion { get; set; }
		public string ArtifactFileName { get; set; }
		public string ArtifactContentType { get; set; }
		public string ArtifactBase64 { get; set; }
	}

	public class CloseoutRecordDeploymentInput
	{
		public long RowVersion { get; set; }
		public string Notes { get; set; }
	}

	#endregion

	#region Reveal

	public class RecordRevealInput
	{
		public string Id { get; set; }
	}

	public class RecordRevealData
	{
		public bool Success { get; set; }
		public string Error { get; set; }
		public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
	}

	public class RecordRevealApiResult : StandardApiResponseV4Base
	{
		public RecordRevealData Data { get; set; }
	}

	#endregion
}
