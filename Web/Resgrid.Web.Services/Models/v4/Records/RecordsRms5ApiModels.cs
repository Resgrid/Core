using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Models.v4.Records
{
	// RMS-5 prevention and investigations plus the RMS-4 quality review and release telemetry (plan sections 4.3, 4.4,
	// 4.7 and 6). Same conventions as the RMS-1B surfaces: envelopes here, contracts in the Model. Cataloged text
	// arrives already revealed or as the REDACTED sentinel; RedactedFields on the aggregate names what was withheld.

	#region Occupancies

	public class OccupancyData
	{
		public string OccupancyId { get; set; }
		public string OccupancyNumber { get; set; }
		public string Name { get; set; }
		public int Status { get; set; }
		public string MergedIntoOccupancyId { get; set; }
		public string AddressText { get; set; }
		public string City { get; set; }
		public string StateProvince { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string ParcelId { get; set; }
		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public int? Stories { get; set; }
		public int? YearBuilt { get; set; }
		public int? SquareFeet { get; set; }
		public int? OccupantLoad { get; set; }
		public string OccupancyHours { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }
		public int SprinklerType { get; set; }
		public bool HasStandpipe { get; set; }
		public bool HasFireAlarm { get; set; }
		public string FdcLocation { get; set; }
		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }
		public string KnoxBoxLocation { get; set; }
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }
		public string NearestHydrantId { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }
		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }
		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }
		public int? PoiId { get; set; }
		public DateTime? LastReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }
		public DateTime? NextReviewDue { get; set; }
		public bool IsReviewOverdue { get; set; }
		public DateTime? LastInspectedOn { get; set; }
		public bool IsProtected { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
	}

	public class OccupancyHazardData
	{
		public string HazardId { get; set; }
		public string OccupancyId { get; set; }
		public int HazardType { get; set; }
		public int Severity { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
		public string SourceContactPreplanHazardId { get; set; }
		public bool IsProtected { get; set; }
	}

	public class OccupancyContactLinkData
	{
		public string LinkId { get; set; }
		public string OccupancyId { get; set; }
		public string ContactId { get; set; }
		public int Role { get; set; }
		public bool IsPrimary { get; set; }
	}

	public class OccupancyProvenanceData
	{
		public string FieldKey { get; set; }
		public int SourceKind { get; set; }
		public string SourceId { get; set; }
		public DateTime CapturedOn { get; set; }
		public DateTime? ReviewedOn { get; set; }
	}

	public class OccupancyCrosswalkData
	{
		public string CrosswalkId { get; set; }
		public string OccupancyId { get; set; }
		public int SourceKind { get; set; }
		public string SourceId { get; set; }
		public string ContactId { get; set; }
		public string SourceDisplayName { get; set; }
		public string NormalizedAddress { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public int MatchConfidence { get; set; }
		public string MatchReason { get; set; }
		public string SuggestedOccupancyId { get; set; }
		public string GroupKey { get; set; }
		public int State { get; set; }
		public DateTime? DecidedOn { get; set; }
		public DateTime InventoriedOn { get; set; }
	}

	public class OccupancyAggregateData
	{
		public OccupancyData Occupancy { get; set; }
		public List<OccupancyHazardData> Hazards { get; set; } = new List<OccupancyHazardData>();
		public List<OccupancyContactLinkData> ContactLinks { get; set; } = new List<OccupancyContactLinkData>();
		public List<OccupancyProvenanceData> Provenance { get; set; } = new List<OccupancyProvenanceData>();
		public List<OccupancyCrosswalkData> Crosswalks { get; set; } = new List<OccupancyCrosswalkData>();
		public int OpenViolationCount { get; set; }
		public bool IsProtected { get; set; }
		public string ProtectedReason { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();
	}

	public class OccupancyInput
	{
		public string OccupancyId { get; set; }
		public long RowVersion { get; set; }
		public string Name { get; set; }
		public int Status { get; set; }
		public string AddressText { get; set; }
		public string City { get; set; }
		public string StateProvince { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string ParcelId { get; set; }
		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public int? Stories { get; set; }
		public int? YearBuilt { get; set; }
		public int? SquareFeet { get; set; }
		public int? OccupantLoad { get; set; }
		public string OccupancyHours { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }
		public int SprinklerType { get; set; }
		public bool HasStandpipe { get; set; }
		public bool HasFireAlarm { get; set; }
		public string FdcLocation { get; set; }
		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }
		public string KnoxBoxLocation { get; set; }
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }
		public string NearestHydrantId { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }
		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }
		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }
		public int? PoiId { get; set; }
		public DateTime? NextReviewDue { get; set; }
	}

	public class OccupancyHazardInput
	{
		public string HazardId { get; set; }
		public string OccupancyId { get; set; }
		public int HazardType { get; set; }
		public int Severity { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
	}

	public class OccupancyContactLinkInput
	{
		public string OccupancyId { get; set; }
		public string ContactId { get; set; }
		public int Role { get; set; }
		public bool IsPrimary { get; set; }
	}

	public class OccupancyReconciliationData
	{
		public string State { get; set; }
		public DateTime? InventoriedOn { get; set; }
		public DateTime? SwitchedOn { get; set; }
		public int Candidates { get; set; }
		public int Linked { get; set; }
		public int Rejected { get; set; }
		public int Occupancies { get; set; }
		public int UnreconciledPreplans { get; set; }
		public bool CanSwitchToRecords { get; set; }
	}

	public class OccupanciesResult : StandardApiResponseV4Base { public List<OccupancyData> Data { get; set; } = new List<OccupancyData>(); public int TotalCount { get; set; } }
	public class OccupancyResult : StandardApiResponseV4Base { public OccupancyAggregateData Data { get; set; } }
	public class OccupancySavedResult : StandardApiResponseV4Base { public OccupancyData Data { get; set; } }
	public class OccupancyHazardResult : StandardApiResponseV4Base { public OccupancyHazardData Data { get; set; } }
	public class OccupancyContactLinkResult : StandardApiResponseV4Base { public OccupancyContactLinkData Data { get; set; } }
	public class OccupancyCrosswalksResult : StandardApiResponseV4Base { public List<OccupancyCrosswalkData> Data { get; set; } = new List<OccupancyCrosswalkData>(); }
	public class OccupancyInventoryResult : StandardApiResponseV4Base { public OccupancyCrosswalkInventoryResult Data { get; set; } }
	public class OccupancyReconciliationResult : StandardApiResponseV4Base { public OccupancyReconciliationData Data { get; set; } }
	public class OccupancyProjectionResult : StandardApiResponseV4Base { public OccupancyDispatchProjectionV1 Data { get; set; } }

	#endregion

	#region Inspections

	public class CodeSetData { public string CodeSetId { get; set; } public string Name { get; set; } public string Edition { get; set; } public string Jurisdiction { get; set; } public bool IsActive { get; set; } }
	public class CodeSectionData { public string CodeSectionId { get; set; } public string CodeSetId { get; set; } public string SectionNumber { get; set; } public string Title { get; set; } public string Text { get; set; } public int DefaultSeverity { get; set; } public int DefaultCorrectionDays { get; set; } }
	public class InspectionProgramData { public string ProgramId { get; set; } public string Name { get; set; } public string Description { get; set; } public string OccupancyTypesCsv { get; set; } public int FrequencyMonths { get; set; } public string CodeSetId { get; set; } public bool IsActive { get; set; } public List<RmsInspectionChecklistItem> Checklist { get; set; } = new List<RmsInspectionChecklistItem>(); }
	public class InspectionProgramInput { public string ProgramId { get; set; } public string Name { get; set; } public string Description { get; set; } public string OccupancyTypesCsv { get; set; } public int FrequencyMonths { get; set; } public string CodeSetId { get; set; } public bool IsActive { get; set; } = true; public List<RmsInspectionChecklistItem> Checklist { get; set; } = new List<RmsInspectionChecklistItem>(); }

	public class InspectionData
	{
		public string InspectionId { get; set; }
		public string OccupancyId { get; set; }
		public string ProgramId { get; set; }
		public string InspectionNumber { get; set; }
		public int State { get; set; }
		public int Result { get; set; }
		public DateTime? ScheduledOn { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string InspectorUserId { get; set; }
		public string Notes { get; set; }
		public string SignatureName { get; set; }
		public DateTime? SignedOn { get; set; }
		public string ParentInspectionId { get; set; }
		public DateTime? NoticeIssuedOn { get; set; }
		public string NoticeReference { get; set; }
		public bool IsProtected { get; set; }
		public long RowVersion { get; set; }
	}

	public class ViolationData
	{
		public string ViolationId { get; set; }
		public string InspectionId { get; set; }
		public string OccupancyId { get; set; }
		public string CodeSetId { get; set; }
		public string CodeSectionId { get; set; }
		public string ChecklistItemKey { get; set; }
		public string Description { get; set; }
		public int Severity { get; set; }
		public string CorrectiveAction { get; set; }
		public DateTime? DueOn { get; set; }
		public int State { get; set; }
		public DateTime? CorrectedOn { get; set; }
		public DateTime? VerifiedOn { get; set; }
		public string ReinspectionId { get; set; }
		public bool IsProtected { get; set; }
	}

	public class InspectionAggregateData
	{
		public InspectionData Inspection { get; set; }
		public InspectionProgramData Program { get; set; }
		public OccupancyData Occupancy { get; set; }
		public List<RmsInspectionItemResult> Items { get; set; } = new List<RmsInspectionItemResult>();
		public List<ViolationData> Violations { get; set; } = new List<ViolationData>();
		public List<PreventionAttachmentData> Attachments { get; set; } = new List<PreventionAttachmentData>();
		public bool IsProtected { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();
	}

	public class ScheduleInspectionInput { public string OccupancyId { get; set; } public string ProgramId { get; set; } public DateTime? ScheduledOn { get; set; } public string InspectorUserId { get; set; } }
	public class CompleteInspectionInput { public string InspectionId { get; set; } public List<RmsInspectionItemResult> Items { get; set; } = new List<RmsInspectionItemResult>(); public string Notes { get; set; } public string SignatureName { get; set; } }
	public class ViolationInput { public string ViolationId { get; set; } public string InspectionId { get; set; } public string CodeSetId { get; set; } public string CodeSectionId { get; set; } public string Description { get; set; } public int Severity { get; set; } public string CorrectiveAction { get; set; } public DateTime? DueOn { get; set; } }
	public class ViolationTransitionInput { public string ViolationId { get; set; } public int TargetState { get; set; } public string Note { get; set; } }

	public class CodeSetsResult : StandardApiResponseV4Base { public List<CodeSetData> Data { get; set; } = new List<CodeSetData>(); }
	public class CodeSetResult : StandardApiResponseV4Base { public CodeSetData Data { get; set; } }
	public class CodeSectionsResult : StandardApiResponseV4Base { public List<CodeSectionData> Data { get; set; } = new List<CodeSectionData>(); }
	public class CodeSectionResult : StandardApiResponseV4Base { public CodeSectionData Data { get; set; } }
	public class InspectionProgramsResult : StandardApiResponseV4Base { public List<InspectionProgramData> Data { get; set; } = new List<InspectionProgramData>(); }
	public class InspectionProgramResult : StandardApiResponseV4Base { public InspectionProgramData Data { get; set; } }
	public class InspectionsResult : StandardApiResponseV4Base { public List<InspectionData> Data { get; set; } = new List<InspectionData>(); public int TotalCount { get; set; } }
	public class InspectionResult : StandardApiResponseV4Base { public InspectionAggregateData Data { get; set; } }
	public class InspectionSavedResult : StandardApiResponseV4Base { public InspectionData Data { get; set; } }
	public class ViolationsResult : StandardApiResponseV4Base { public List<ViolationData> Data { get; set; } = new List<ViolationData>(); }
	public class ViolationResult : StandardApiResponseV4Base { public ViolationData Data { get; set; } }

	#endregion

	#region Hydrants

	public class HydrantData
	{
		public string HydrantId { get; set; }
		public string HydrantNumber { get; set; }
		public int Type { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string AddressText { get; set; }
		public int OwnerKind { get; set; }
		public string OwnerName { get; set; }
		public decimal? MainSizeInches { get; set; }
		public int? StaticPressurePsi { get; set; }
		public int? ResidualPressurePsi { get; set; }
		public int? FlowGpm { get; set; }
		public int FlowClass { get; set; }
		public bool InService { get; set; }
		public string OutOfServiceReason { get; set; }
		public DateTime? OutOfServiceSince { get; set; }
		public DateTime? LastTestedOn { get; set; }
		public DateTime? LastMaintainedOn { get; set; }
		public string Notes { get; set; }
		public int? PoiId { get; set; }
		public string Source { get; set; }
		public long RowVersion { get; set; }
	}

	public class HydrantFlowTestData { public string FlowTestId { get; set; } public string HydrantId { get; set; } public DateTime TestedOn { get; set; } public string TestedByUserId { get; set; } public int StaticPressurePsi { get; set; } public int ResidualPressurePsi { get; set; } public int PitotPressurePsi { get; set; } public decimal OutletDiameterInches { get; set; } public decimal Coefficient { get; set; } public int FlowGpm { get; set; } public int FlowClass { get; set; } public string Notes { get; set; } }
	public class HydrantMaintenanceData { public string MaintenanceId { get; set; } public string HydrantId { get; set; } public DateTime PerformedOn { get; set; } public string PerformedByUserId { get; set; } public int Kind { get; set; } public string Notes { get; set; } public bool ReturnedToService { get; set; } }
	public class HydrantAggregateData { public HydrantData Hydrant { get; set; } public List<HydrantFlowTestData> FlowTests { get; set; } = new List<HydrantFlowTestData>(); public List<HydrantMaintenanceData> Maintenance { get; set; } = new List<HydrantMaintenanceData>(); public List<PreventionAttachmentData> Attachments { get; set; } = new List<PreventionAttachmentData>(); }
	public class HydrantInput { public string HydrantId { get; set; } public string HydrantNumber { get; set; } public int Type { get; set; } public decimal Latitude { get; set; } public decimal Longitude { get; set; } public string AddressText { get; set; } public int OwnerKind { get; set; } public string OwnerName { get; set; } public decimal? MainSizeInches { get; set; } public int? FlowGpm { get; set; } public int? StaticPressurePsi { get; set; } public int? ResidualPressurePsi { get; set; } public string Notes { get; set; } public int? PoiId { get; set; } }
	public class HydrantServiceStateInput { public string HydrantId { get; set; } public bool InService { get; set; } public string Reason { get; set; } }
	public class HydrantFlowTestInput { public string HydrantId { get; set; } public DateTime? TestedOn { get; set; } public int StaticPressurePsi { get; set; } public int ResidualPressurePsi { get; set; } public int PitotPressurePsi { get; set; } public decimal OutletDiameterInches { get; set; } public decimal Coefficient { get; set; } public string Notes { get; set; } }
	public class HydrantMaintenanceInput { public string HydrantId { get; set; } public DateTime? PerformedOn { get; set; } public int Kind { get; set; } public string Notes { get; set; } public bool ReturnedToService { get; set; } }
	public class HydrantImportInput { public string Csv { get; set; } }

	public class HydrantsResult : StandardApiResponseV4Base { public List<HydrantData> Data { get; set; } = new List<HydrantData>(); }
	public class HydrantResult : StandardApiResponseV4Base { public HydrantAggregateData Data { get; set; } }
	public class HydrantSavedResult : StandardApiResponseV4Base { public HydrantData Data { get; set; } }
	public class HydrantFlowTestResult : StandardApiResponseV4Base { public HydrantFlowTestData Data { get; set; } }
	public class HydrantMaintenanceResult : StandardApiResponseV4Base { public HydrantMaintenanceData Data { get; set; } }
	public class HydrantImportResultData : StandardApiResponseV4Base { public HydrantImportResult Data { get; set; } }
	public class HydrantMapLayerResult : StandardApiResponseV4Base { public List<HydrantMapPoint> Data { get; set; } = new List<HydrantMapPoint>(); }

	#endregion

	#region Permits

	public class PermitTypeData { public string PermitTypeId { get; set; } public string Name { get; set; } public string Code { get; set; } public string Description { get; set; } public int DefaultValidityDays { get; set; } public bool RequiresPlanReview { get; set; } public decimal? FeeAmount { get; set; } public string ConditionsTemplate { get; set; } public bool IsActive { get; set; } }
	public class PermitData
	{
		public string PermitId { get; set; }
		public string PermitTypeId { get; set; }
		public string OccupancyId { get; set; }
		public string PermitNumber { get; set; }
		public string ApplicantContactId { get; set; }
		public string ApplicantName { get; set; }
		public string ApplicantPhone { get; set; }
		public string ApplicantEmail { get; set; }
		public string Description { get; set; }
		public int State { get; set; }
		public DateTime AppliedOn { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public string Conditions { get; set; }
		public string ReviewNotes { get; set; }
		public decimal? FeeAmount { get; set; }
		public DateTime? FeePaidOn { get; set; }
		public string InvoiceReference { get; set; }
		public string DecisionReason { get; set; }
		public bool IsProtected { get; set; }
		public long RowVersion { get; set; }
	}
	public class PlanReviewData { public string PlanReviewId { get; set; } public string PermitId { get; set; } public int CycleNumber { get; set; } public DateTime SubmittedOn { get; set; } public string ReviewerUserId { get; set; } public DateTime? ReviewedOn { get; set; } public int Outcome { get; set; } public string Comments { get; set; } }
	public class PermitAggregateData { public PermitData Permit { get; set; } public PermitTypeData Type { get; set; } public OccupancyData Occupancy { get; set; } public List<PlanReviewData> PlanReviews { get; set; } = new List<PlanReviewData>(); public List<PreventionAttachmentData> Attachments { get; set; } = new List<PreventionAttachmentData>(); public bool IsProtected { get; set; } public List<string> RedactedFields { get; set; } = new List<string>(); }
	public class PermitInput { public string PermitId { get; set; } public long RowVersion { get; set; } public string PermitTypeId { get; set; } public string OccupancyId { get; set; } public string ApplicantContactId { get; set; } public string ApplicantName { get; set; } public string ApplicantPhone { get; set; } public string ApplicantEmail { get; set; } public string Description { get; set; } public string Conditions { get; set; } public string ReviewNotes { get; set; } public decimal? FeeAmount { get; set; } public DateTime? EffectiveOn { get; set; } public DateTime? ExpiresOn { get; set; } }
	public class PermitTransitionInput { public string PermitId { get; set; } public int TargetState { get; set; } public string Reason { get; set; } public DateTime? EffectiveOn { get; set; } public DateTime? ExpiresOn { get; set; } }
	public class PlanReviewInput { public string PermitId { get; set; } public int Outcome { get; set; } public string Comments { get; set; } }
	public class PermitFeeInput { public string PermitId { get; set; } public decimal Amount { get; set; } public string InvoiceReference { get; set; } }

	public class PermitTypesResult : StandardApiResponseV4Base { public List<PermitTypeData> Data { get; set; } = new List<PermitTypeData>(); }
	public class PermitTypeResult : StandardApiResponseV4Base { public PermitTypeData Data { get; set; } }
	public class PermitsResult : StandardApiResponseV4Base { public List<PermitData> Data { get; set; } = new List<PermitData>(); public int TotalCount { get; set; } }
	public class PermitResult : StandardApiResponseV4Base { public PermitAggregateData Data { get; set; } }
	public class PermitSavedResult : StandardApiResponseV4Base { public PermitData Data { get; set; } }
	public class PlanReviewResult : StandardApiResponseV4Base { public PlanReviewData Data { get; set; } }

	#endregion

	#region CRR

	public class CrrActivityData { public string ActivityId { get; set; } public int Kind { get; set; } public DateTime OccurredOn { get; set; } public string Title { get; set; } public string Description { get; set; } public string OccupancyId { get; set; } public string LocationText { get; set; } public decimal? Latitude { get; set; } public decimal? Longitude { get; set; } public int AudienceCount { get; set; } public int SmokeAlarmsInstalled { get; set; } public decimal HoursSpent { get; set; } public string StaffUserIdsCsv { get; set; } public string Outcome { get; set; } public long RowVersion { get; set; } }
	public class CrrActivityInput { public string ActivityId { get; set; } public int Kind { get; set; } public DateTime? OccurredOn { get; set; } public string Title { get; set; } public string Description { get; set; } public string OccupancyId { get; set; } public string LocationText { get; set; } public decimal? Latitude { get; set; } public decimal? Longitude { get; set; } public int AudienceCount { get; set; } public int SmokeAlarmsInstalled { get; set; } public decimal HoursSpent { get; set; } public string StaffUserIdsCsv { get; set; } public string Outcome { get; set; } }
	public class CrrActivitiesResult : StandardApiResponseV4Base { public List<CrrActivityData> Data { get; set; } = new List<CrrActivityData>(); }
	public class CrrActivityResult : StandardApiResponseV4Base { public CrrActivityData Data { get; set; } }
	public class CrrSummaryResult : StandardApiResponseV4Base { public CrrSummary Data { get; set; } }

	#endregion

	#region Attachments

	public class PreventionAttachmentData { public string AttachmentId { get; set; } public int ParentKind { get; set; } public string ParentId { get; set; } public string FileName { get; set; } public string ContentType { get; set; } public long ByteSize { get; set; } public string Checksum { get; set; } public string Description { get; set; } public string UploadedByUserId { get; set; } public DateTime UploadedOn { get; set; } public int ScanState { get; set; } public int Classification { get; set; } public bool IsProtected { get; set; } public string Data { get; set; } }
	public class PreventionAttachmentInput { public int ParentKind { get; set; } public string ParentId { get; set; } public string FileName { get; set; } public string ContentType { get; set; } public string Data { get; set; } public string Description { get; set; } }
	public class PreventionAttachmentsResult : StandardApiResponseV4Base { public List<PreventionAttachmentData> Data { get; set; } = new List<PreventionAttachmentData>(); }
	public class PreventionAttachmentResult : StandardApiResponseV4Base { public PreventionAttachmentData Data { get; set; } }

	#endregion

	#region Investigations

	public class InvestigationCaseData
	{
		public string CaseId { get; set; }
		public string CaseNumber { get; set; }
		public string Title { get; set; }
		public int State { get; set; }
		public DateTime OpenedOn { get; set; }
		public string OpenedByUserId { get; set; }
		public string LeadInvestigatorUserId { get; set; }
		public string OccupancyId { get; set; }
		public int? CallId { get; set; }
		public string IncidentSummary { get; set; }
		public int CauseClassification { get; set; }
		public string CauseDetail { get; set; }
		public string OriginDescription { get; set; }
		public string Findings { get; set; }
		public string FindingsAuthorUserId { get; set; }
		public DateTime? FindingsRecordedOn { get; set; }
		public DateTime? FindingsApprovedOn { get; set; }
		public string FindingsApprovedByUserId { get; set; }
		public bool RecommendsIncidentAmendment { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string ClosedByUserId { get; set; }
		public string ClosureReason { get; set; }
		public bool IsProtected { get; set; }
		public long RowVersion { get; set; }
	}
	public class CaseMemberData { public string MemberId { get; set; } public string UserId { get; set; } public int Role { get; set; } public DateTime AddedOn { get; set; } public DateTime? RemovedOn { get; set; } }
	public class CaseIncidentData { public string LinkId { get; set; } public string RecordId { get; set; } public string PinnedRevisionId { get; set; } public string RecordNumber { get; set; } public DateTime LinkedOn { get; set; } }
	public class InvestigationNoteData { public string NoteId { get; set; } public int Kind { get; set; } public DateTime OccurredOn { get; set; } public string AuthorUserId { get; set; } public string Subject { get; set; } public string Body { get; set; } public bool IsLocked { get; set; } public bool IsProtected { get; set; } }
	public class InvestigationEvidenceData { public string EvidenceId { get; set; } public string EvidenceNumber { get; set; } public int Kind { get; set; } public string Description { get; set; } public DateTime CollectedOn { get; set; } public string CollectedByUserId { get; set; } public string CollectedFrom { get; set; } public int State { get; set; } public string CurrentCustodianUserId { get; set; } public string CurrentCustodianExternal { get; set; } public string StorageLocation { get; set; } public bool IsProtected { get; set; } }
	public class CustodyData { public string CustodyId { get; set; } public string EvidenceId { get; set; } public int Sequence { get; set; } public DateTime TransferredOn { get; set; } public string FromUserId { get; set; } public string FromExternal { get; set; } public string ToUserId { get; set; } public string ToExternal { get; set; } public string Reason { get; set; } public int ResultingState { get; set; } public string RecordedByUserId { get; set; } }
	public class ReferralData { public string ReferralId { get; set; } public string Agency { get; set; } public DateTime ReferredOn { get; set; } public string ReferredByUserId { get; set; } public string Reason { get; set; } public string ReferenceNumber { get; set; } public int State { get; set; } }
	public class InvestigationCaseAggregateData
	{
		public InvestigationCaseData Case { get; set; }
		public int? CallerRole { get; set; }
		public List<CaseMemberData> Members { get; set; } = new List<CaseMemberData>();
		public List<CaseIncidentData> Incidents { get; set; } = new List<CaseIncidentData>();
		public List<InvestigationNoteData> Notes { get; set; } = new List<InvestigationNoteData>();
		public List<InvestigationEvidenceData> Evidence { get; set; } = new List<InvestigationEvidenceData>();
		public List<ReferralData> Referrals { get; set; } = new List<ReferralData>();
		public List<PreventionAttachmentData> Attachments { get; set; } = new List<PreventionAttachmentData>();
		public bool IsProtected { get; set; }
		public string ProtectedReason { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();
	}
	public class OpenCaseInput { public string Title { get; set; } public string OccupancyId { get; set; } public int? CallId { get; set; } public string IncidentSummary { get; set; } }
	public class UpdateCaseInput { public string CaseId { get; set; } public long RowVersion { get; set; } public string Title { get; set; } public string IncidentSummary { get; set; } public string OccupancyId { get; set; } public int? CallId { get; set; } public string LeadInvestigatorUserId { get; set; } }
	public class CaseMemberInput { public string CaseId { get; set; } public string UserId { get; set; } public int Role { get; set; } }
	public class CaseNoteInput { public string CaseId { get; set; } public string NoteId { get; set; } public int Kind { get; set; } public DateTime? OccurredOn { get; set; } public string Subject { get; set; } public string Body { get; set; } }
	public class CaseEvidenceInput { public string CaseId { get; set; } public int Kind { get; set; } public string Description { get; set; } public DateTime? CollectedOn { get; set; } public string CollectedByUserId { get; set; } public string CollectedFrom { get; set; } public string StorageLocation { get; set; } }
	public class CustodyTransferInput { public string EvidenceId { get; set; } public string ToUserId { get; set; } public string ToExternal { get; set; } public string Reason { get; set; } public int ResultingState { get; set; } }
	public class ReferralInput { public string CaseId { get; set; } public string Agency { get; set; } public string Reason { get; set; } public string ReferenceNumber { get; set; } }
	public class FindingsInput { public string CaseId { get; set; } public int Classification { get; set; } public string CauseDetail { get; set; } public string OriginDescription { get; set; } public string Findings { get; set; } public bool RecommendsIncidentAmendment { get; set; } }
	public class CaseReasonInput { public string CaseId { get; set; } public string Reason { get; set; } }

	public class InvestigationCasesResult : StandardApiResponseV4Base { public List<InvestigationCaseData> Data { get; set; } = new List<InvestigationCaseData>(); }
	public class InvestigationCaseResult : StandardApiResponseV4Base { public InvestigationCaseAggregateData Data { get; set; } }
	public class InvestigationCaseSavedResult : StandardApiResponseV4Base { public InvestigationCaseData Data { get; set; } }
	public class CaseMemberResult : StandardApiResponseV4Base { public CaseMemberData Data { get; set; } }
	public class CaseIncidentResult : StandardApiResponseV4Base { public CaseIncidentData Data { get; set; } }
	public class InvestigationNoteResult : StandardApiResponseV4Base { public InvestigationNoteData Data { get; set; } }
	public class InvestigationEvidenceResult : StandardApiResponseV4Base { public InvestigationEvidenceData Data { get; set; } }
	public class CustodyResult : StandardApiResponseV4Base { public CustodyData Data { get; set; } }
	public class CustodyChainResult : StandardApiResponseV4Base { public List<CustodyData> Data { get; set; } = new List<CustodyData>(); }
	public class ReferralResult : StandardApiResponseV4Base { public ReferralData Data { get; set; } }
	public class CaseAuditResult : StandardApiResponseV4Base { public List<CaseAuditEntryData> Data { get; set; } = new List<CaseAuditEntryData>(); }
	public class CaseAuditEntryData { public int Action { get; set; } public string ActorUserId { get; set; } public string Purpose { get; set; } public bool Successful { get; set; } public DateTime OccurredOn { get; set; } public string DetailJson { get; set; } }

	#endregion

	#region Quality review and health

	public class QualityRubricData { public string RubricId { get; set; } public string Name { get; set; } public string DefinitionKey { get; set; } public int SampleSize { get; set; } public bool IsActive { get; set; } public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>(); }
	public class QualityRubricInput { public string RubricId { get; set; } public string Name { get; set; } public string DefinitionKey { get; set; } public int SampleSize { get; set; } public bool IsActive { get; set; } = true; public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>(); }
	public class QualityReviewData { public string ReviewId { get; set; } public string RubricId { get; set; } public string RecordId { get; set; } public int RecordKind { get; set; } public string RevisionId { get; set; } public string DefinitionKey { get; set; } public string RecordNumber { get; set; } public string AuthorUserId { get; set; } public int? UnitId { get; set; } public string ReviewerUserId { get; set; } public DateTime SampledOn { get; set; } public DateTime? ScoredOn { get; set; } public int? Score { get; set; } public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>(); public List<RmsQualityFinding> Findings { get; set; } = new List<RmsQualityFinding>(); public string Note { get; set; } public bool AmendmentRecommended { get; set; } public bool IsProtected { get; set; } }
	public class QualitySampleInput { public string RubricId { get; set; } public DateTime? Since { get; set; } }
	public class QualityScoreInput { public string ReviewId { get; set; } public List<RmsQualityFinding> Findings { get; set; } = new List<RmsQualityFinding>(); public string Note { get; set; } public bool AmendmentRecommended { get; set; } }
	public class QualityRubricsResult : StandardApiResponseV4Base { public List<QualityRubricData> Data { get; set; } = new List<QualityRubricData>(); }
	public class QualityRubricResult : StandardApiResponseV4Base { public QualityRubricData Data { get; set; } }
	public class QualityReviewsResult : StandardApiResponseV4Base { public List<QualityReviewData> Data { get; set; } = new List<QualityReviewData>(); }
	public class QualityReviewResult : StandardApiResponseV4Base { public QualityReviewData Data { get; set; } }
	public class QualityTrendsResult : StandardApiResponseV4Base { public RecordsQualityTrends Data { get; set; } }
	public class RecordsReleaseTelemetryResult : StandardApiResponseV4Base { public RecordsReleaseTelemetry Data { get; set; } }
	public class PreventionSummaryResult : StandardApiResponseV4Base { public PreventionSummary Data { get; set; } }

	#endregion

	/// <summary>Model → v4 mapping. Entities never cross the wire (IEntity metadata, ProtectionId and attachment bytes stay server-side).</summary>
	public static class RecordsRms5ApiMapper
	{
		public static OccupancyData ToOccupancy(RmsOccupancy o) => o == null ? null : new OccupancyData
		{
			OccupancyId = o.RmsOccupancyId, OccupancyNumber = o.OccupancyNumber, Name = o.Name, Status = o.Status, MergedIntoOccupancyId = o.MergedIntoOccupancyId, AddressText = o.AddressText, City = o.City, StateProvince = o.StateProvince, PostalCode = o.PostalCode, Country = o.Country,
			Latitude = o.Latitude, Longitude = o.Longitude, ParcelId = o.ParcelId, ConstructionType = o.ConstructionType, RoofType = o.RoofType, OccupancyType = o.OccupancyType, Stories = o.Stories, YearBuilt = o.YearBuilt, SquareFeet = o.SquareFeet, OccupantLoad = o.OccupantLoad,
			OccupancyHours = o.OccupancyHours, HasOccupantsNeedingAssistance = o.HasOccupantsNeedingAssistance, OccupantsNeedingAssistanceNotes = o.OccupantsNeedingAssistanceNotes, SprinklerType = o.SprinklerType, HasStandpipe = o.HasStandpipe, HasFireAlarm = o.HasFireAlarm, FdcLocation = o.FdcLocation,
			GasShutoffLocation = o.GasShutoffLocation, ElectricShutoffLocation = o.ElectricShutoffLocation, WaterShutoffLocation = o.WaterShutoffLocation, UtilityNotes = o.UtilityNotes, KnoxBoxLocation = o.KnoxBoxLocation, GateCode = o.GateCode, AlarmPanelLocation = o.AlarmPanelLocation,
			AlarmCompany = o.AlarmCompany, AlarmCompanyPhone = o.AlarmCompanyPhone, AccessNotes = o.AccessNotes, NearestHydrantId = o.NearestHydrantId, RequiredFireFlowGpm = o.RequiredFireFlowGpm, WaterSupplyNotes = o.WaterSupplyNotes, EmergencyContactName = o.EmergencyContactName, EmergencyContactPhone = o.EmergencyContactPhone,
			HazmatOnSite = o.HazmatOnSite, GeneralHazardNotes = o.GeneralHazardNotes, TacticalSummary = o.TacticalSummary, PoiId = o.PoiId, LastReviewedOn = o.LastReviewedOn, ReviewedByUserId = o.ReviewedByUserId, NextReviewDue = o.NextReviewDue, IsReviewOverdue = o.IsReviewOverdue(DateTime.UtcNow), LastInspectedOn = o.LastInspectedOn,
			IsProtected = o.IsProtected, CreatedOn = o.CreatedOn, ModifiedOn = o.ModifiedOn, RowVersion = o.RowVersion
		};

		public static RmsOccupancy FromOccupancy(OccupancyInput i) => new RmsOccupancy
		{
			RmsOccupancyId = i.OccupancyId, RowVersion = i.RowVersion, Name = i.Name, Status = i.Status, AddressText = i.AddressText, City = i.City, StateProvince = i.StateProvince, PostalCode = i.PostalCode, Country = i.Country, Latitude = i.Latitude, Longitude = i.Longitude, ParcelId = i.ParcelId,
			ConstructionType = i.ConstructionType, RoofType = i.RoofType, OccupancyType = i.OccupancyType, Stories = i.Stories, YearBuilt = i.YearBuilt, SquareFeet = i.SquareFeet, OccupantLoad = i.OccupantLoad, OccupancyHours = i.OccupancyHours, HasOccupantsNeedingAssistance = i.HasOccupantsNeedingAssistance,
			OccupantsNeedingAssistanceNotes = i.OccupantsNeedingAssistanceNotes, SprinklerType = i.SprinklerType, HasStandpipe = i.HasStandpipe, HasFireAlarm = i.HasFireAlarm, FdcLocation = i.FdcLocation, GasShutoffLocation = i.GasShutoffLocation, ElectricShutoffLocation = i.ElectricShutoffLocation, WaterShutoffLocation = i.WaterShutoffLocation,
			UtilityNotes = i.UtilityNotes, KnoxBoxLocation = i.KnoxBoxLocation, GateCode = i.GateCode, AlarmPanelLocation = i.AlarmPanelLocation, AlarmCompany = i.AlarmCompany, AlarmCompanyPhone = i.AlarmCompanyPhone, AccessNotes = i.AccessNotes, NearestHydrantId = i.NearestHydrantId, RequiredFireFlowGpm = i.RequiredFireFlowGpm,
			WaterSupplyNotes = i.WaterSupplyNotes, EmergencyContactName = i.EmergencyContactName, EmergencyContactPhone = i.EmergencyContactPhone, HazmatOnSite = i.HazmatOnSite, GeneralHazardNotes = i.GeneralHazardNotes, TacticalSummary = i.TacticalSummary, PoiId = i.PoiId, NextReviewDue = i.NextReviewDue
		};

		public static OccupancyHazardData ToHazard(RmsOccupancyHazard h) => h == null ? null : new OccupancyHazardData { HazardId = h.RmsOccupancyHazardId, OccupancyId = h.RmsOccupancyId, HazardType = h.HazardType, Severity = h.Severity, Title = h.Title, Description = h.Description, LocationDescription = h.LocationDescription, GpsCoordinates = h.GpsCoordinates, ShouldAlert = h.ShouldAlert, SourceContactPreplanHazardId = h.SourceContactPreplanHazardId, IsProtected = h.IsProtected };
		public static OccupancyContactLinkData ToLink(RmsOccupancyContactLink l) => l == null ? null : new OccupancyContactLinkData { LinkId = l.RmsOccupancyContactLinkId, OccupancyId = l.RmsOccupancyId, ContactId = l.ContactId, Role = l.Role, IsPrimary = l.IsPrimary };
		public static OccupancyCrosswalkData ToCrosswalk(RmsOccupancyCrosswalk c) => c == null ? null : new OccupancyCrosswalkData { CrosswalkId = c.RmsOccupancyCrosswalkId, OccupancyId = c.RmsOccupancyId, SourceKind = c.SourceKind, SourceId = c.SourceId, ContactId = c.ContactId, SourceDisplayName = c.SourceDisplayName, NormalizedAddress = c.NormalizedAddress, Latitude = c.Latitude, Longitude = c.Longitude, MatchConfidence = c.MatchConfidence, MatchReason = c.MatchReason, SuggestedOccupancyId = c.SuggestedOccupancyId, GroupKey = c.GroupKey, State = c.State, DecidedOn = c.DecidedOn, InventoriedOn = c.InventoriedOn };

		public static OccupancyAggregateData ToOccupancyAggregate(OccupancyAggregate a) => a == null ? null : new OccupancyAggregateData
		{
			Occupancy = ToOccupancy(a.Occupancy), Hazards = a.Hazards.Select(ToHazard).ToList(), ContactLinks = a.ContactLinks.Select(ToLink).ToList(),
			Provenance = a.Provenance.Select(p => new OccupancyProvenanceData { FieldKey = p.FieldKey, SourceKind = p.SourceKind, SourceId = p.SourceId, CapturedOn = p.CapturedOn, ReviewedOn = p.ReviewedOn }).ToList(),
			Crosswalks = a.Crosswalks.Select(ToCrosswalk).ToList(), OpenViolationCount = a.OpenViolationCount, IsProtected = a.Protection.IsProtected, ProtectedReason = a.Protection.ProtectedReason, RedactedFields = a.Protection.RedactedFields.Distinct().ToList()
		};

		public static OccupancyReconciliationData ToReconciliation(OccupancyReconciliationStatus s) => new OccupancyReconciliationData { State = s.State.ToString(), InventoriedOn = s.InventoriedOn, SwitchedOn = s.SwitchedOn, Candidates = s.Candidates, Linked = s.Linked, Rejected = s.Rejected, Occupancies = s.Occupancies, UnreconciledPreplans = s.UnreconciledPreplans, CanSwitchToRecords = s.CanSwitchToRecords };

		public static CodeSetData ToCodeSet(RmsCodeSet c) => c == null ? null : new CodeSetData { CodeSetId = c.RmsCodeSetId, Name = c.Name, Edition = c.Edition, Jurisdiction = c.Jurisdiction, IsActive = c.IsActive };
		public static CodeSectionData ToCodeSection(RmsCodeSection s) => s == null ? null : new CodeSectionData { CodeSectionId = s.RmsCodeSectionId, CodeSetId = s.RmsCodeSetId, SectionNumber = s.SectionNumber, Title = s.Title, Text = s.Text, DefaultSeverity = s.DefaultSeverity, DefaultCorrectionDays = s.DefaultCorrectionDays };
		public static InspectionProgramData ToProgram(RmsInspectionProgram p) => p == null ? null : new InspectionProgramData { ProgramId = p.RmsInspectionProgramId, Name = p.Name, Description = p.Description, OccupancyTypesCsv = p.OccupancyTypesCsv, FrequencyMonths = p.FrequencyMonths, CodeSetId = p.RmsCodeSetId, IsActive = p.IsActive, Checklist = Resgrid.Services.Records.RecordsInspectionsService.ParseChecklist(p.ChecklistJson) };
		public static InspectionData ToInspection(RmsInspection i) => i == null ? null : new InspectionData { InspectionId = i.RmsInspectionId, OccupancyId = i.RmsOccupancyId, ProgramId = i.RmsInspectionProgramId, InspectionNumber = i.InspectionNumber, State = i.State, Result = i.Result, ScheduledOn = i.ScheduledOn, StartedOn = i.StartedOn, CompletedOn = i.CompletedOn, InspectorUserId = i.InspectorUserId, Notes = i.Notes, SignatureName = i.SignatureName, SignedOn = i.SignedOn, ParentInspectionId = i.ParentInspectionId, NoticeIssuedOn = i.NoticeIssuedOn, NoticeReference = i.NoticeReference, IsProtected = i.IsProtected, RowVersion = i.RowVersion };
		public static ViolationData ToViolation(RmsViolation v) => v == null ? null : new ViolationData { ViolationId = v.RmsViolationId, InspectionId = v.RmsInspectionId, OccupancyId = v.RmsOccupancyId, CodeSetId = v.RmsCodeSetId, CodeSectionId = v.RmsCodeSectionId, ChecklistItemKey = v.ChecklistItemKey, Description = v.Description, Severity = v.Severity, CorrectiveAction = v.CorrectiveAction, DueOn = v.DueOn, State = v.State, CorrectedOn = v.CorrectedOn, VerifiedOn = v.VerifiedOn, ReinspectionId = v.ReinspectionId, IsProtected = v.IsProtected };
		public static InspectionAggregateData ToInspectionAggregate(InspectionAggregate a) => a == null ? null : new InspectionAggregateData { Inspection = ToInspection(a.Inspection), Program = ToProgram(a.Program), Occupancy = ToOccupancy(a.Occupancy), Items = a.Items, Violations = a.Violations.Select(ToViolation).ToList(), Attachments = a.Attachments.Select(x => ToAttachment(x, false)).ToList(), IsProtected = a.Protection.IsProtected, RedactedFields = a.Protection.RedactedFields.Distinct().ToList() };

		public static HydrantData ToHydrant(RmsHydrant h) => h == null ? null : new HydrantData { HydrantId = h.RmsHydrantId, HydrantNumber = h.HydrantNumber, Type = h.Type, Latitude = h.Latitude, Longitude = h.Longitude, AddressText = h.AddressText, OwnerKind = h.OwnerKind, OwnerName = h.OwnerName, MainSizeInches = h.MainSizeInches, StaticPressurePsi = h.StaticPressurePsi, ResidualPressurePsi = h.ResidualPressurePsi, FlowGpm = h.FlowGpm, FlowClass = h.FlowClass, InService = h.InService, OutOfServiceReason = h.OutOfServiceReason, OutOfServiceSince = h.OutOfServiceSince, LastTestedOn = h.LastTestedOn, LastMaintainedOn = h.LastMaintainedOn, Notes = h.Notes, PoiId = h.PoiId, Source = h.Source, RowVersion = h.RowVersion };
		public static HydrantFlowTestData ToFlowTest(RmsHydrantFlowTest t) => t == null ? null : new HydrantFlowTestData { FlowTestId = t.RmsHydrantFlowTestId, HydrantId = t.RmsHydrantId, TestedOn = t.TestedOn, TestedByUserId = t.TestedByUserId, StaticPressurePsi = t.StaticPressurePsi, ResidualPressurePsi = t.ResidualPressurePsi, PitotPressurePsi = t.PitotPressurePsi, OutletDiameterInches = t.OutletDiameterInches, Coefficient = t.Coefficient, FlowGpm = t.FlowGpm, FlowClass = t.FlowClass, Notes = t.Notes };
		public static HydrantMaintenanceData ToMaintenance(RmsHydrantMaintenance m) => m == null ? null : new HydrantMaintenanceData { MaintenanceId = m.RmsHydrantMaintenanceId, HydrantId = m.RmsHydrantId, PerformedOn = m.PerformedOn, PerformedByUserId = m.PerformedByUserId, Kind = m.Kind, Notes = m.Notes, ReturnedToService = m.ReturnedToService };
		public static HydrantAggregateData ToHydrantAggregate(HydrantAggregate a) => a == null ? null : new HydrantAggregateData { Hydrant = ToHydrant(a.Hydrant), FlowTests = a.FlowTests.Select(ToFlowTest).ToList(), Maintenance = a.Maintenance.Select(ToMaintenance).ToList(), Attachments = a.Attachments.Select(x => ToAttachment(x, false)).ToList() };

		public static PermitTypeData ToPermitType(RmsPermitType t) => t == null ? null : new PermitTypeData { PermitTypeId = t.RmsPermitTypeId, Name = t.Name, Code = t.Code, Description = t.Description, DefaultValidityDays = t.DefaultValidityDays, RequiresPlanReview = t.RequiresPlanReview, FeeAmount = t.FeeAmount, ConditionsTemplate = t.ConditionsTemplate, IsActive = t.IsActive };
		public static PermitData ToPermit(RmsPermit p) => p == null ? null : new PermitData { PermitId = p.RmsPermitId, PermitTypeId = p.RmsPermitTypeId, OccupancyId = p.RmsOccupancyId, PermitNumber = p.PermitNumber, ApplicantContactId = p.ApplicantContactId, ApplicantName = p.ApplicantName, ApplicantPhone = p.ApplicantPhone, ApplicantEmail = p.ApplicantEmail, Description = p.Description, State = p.State, AppliedOn = p.AppliedOn, ReviewedOn = p.ReviewedOn, IssuedOn = p.IssuedOn, EffectiveOn = p.EffectiveOn, ExpiresOn = p.ExpiresOn, Conditions = p.Conditions, ReviewNotes = p.ReviewNotes, FeeAmount = p.FeeAmount, FeePaidOn = p.FeePaidOn, InvoiceReference = p.InvoiceReference, DecisionReason = p.DecisionReason, IsProtected = p.IsProtected, RowVersion = p.RowVersion };
		public static PlanReviewData ToPlanReview(RmsPlanReview r) => r == null ? null : new PlanReviewData { PlanReviewId = r.RmsPlanReviewId, PermitId = r.RmsPermitId, CycleNumber = r.CycleNumber, SubmittedOn = r.SubmittedOn, ReviewerUserId = r.ReviewerUserId, ReviewedOn = r.ReviewedOn, Outcome = r.Outcome, Comments = r.Comments };
		public static PermitAggregateData ToPermitAggregate(PermitAggregate a) => a == null ? null : new PermitAggregateData { Permit = ToPermit(a.Permit), Type = ToPermitType(a.Type), Occupancy = ToOccupancy(a.Occupancy), PlanReviews = a.PlanReviews.Select(ToPlanReview).ToList(), Attachments = a.Attachments.Select(x => ToAttachment(x, false)).ToList(), IsProtected = a.Protection.IsProtected, RedactedFields = a.Protection.RedactedFields.Distinct().ToList() };
		public static RmsPermit FromPermit(PermitInput i) => new RmsPermit { RmsPermitId = i.PermitId, RowVersion = i.RowVersion, RmsPermitTypeId = i.PermitTypeId, RmsOccupancyId = i.OccupancyId, ApplicantContactId = i.ApplicantContactId, ApplicantName = i.ApplicantName, ApplicantPhone = i.ApplicantPhone, ApplicantEmail = i.ApplicantEmail, Description = i.Description, Conditions = i.Conditions, ReviewNotes = i.ReviewNotes, FeeAmount = i.FeeAmount, EffectiveOn = i.EffectiveOn, ExpiresOn = i.ExpiresOn };

		public static CrrActivityData ToCrr(RmsCrrActivity a) => a == null ? null : new CrrActivityData { ActivityId = a.RmsCrrActivityId, Kind = a.Kind, OccurredOn = a.OccurredOn, Title = a.Title, Description = a.Description, OccupancyId = a.RmsOccupancyId, LocationText = a.LocationText, Latitude = a.Latitude, Longitude = a.Longitude, AudienceCount = a.AudienceCount, SmokeAlarmsInstalled = a.SmokeAlarmsInstalled, HoursSpent = a.HoursSpent, StaffUserIdsCsv = a.StaffUserIdsCsv, Outcome = a.Outcome, RowVersion = a.RowVersion };
		public static RmsCrrActivity FromCrr(CrrActivityInput i) => new RmsCrrActivity { RmsCrrActivityId = i.ActivityId, Kind = i.Kind, OccurredOn = i.OccurredOn ?? default, Title = i.Title, Description = i.Description, RmsOccupancyId = i.OccupancyId, LocationText = i.LocationText, Latitude = i.Latitude, Longitude = i.Longitude, AudienceCount = i.AudienceCount, SmokeAlarmsInstalled = i.SmokeAlarmsInstalled, HoursSpent = i.HoursSpent, StaffUserIdsCsv = i.StaffUserIdsCsv, Outcome = i.Outcome };

		public static PreventionAttachmentData ToAttachment(RmsPreventionAttachment a, bool includeData) => a == null ? null : new PreventionAttachmentData { AttachmentId = a.RmsPreventionAttachmentId, ParentKind = a.ParentKind, ParentId = a.ParentId, FileName = a.FileName, ContentType = a.ContentType, ByteSize = a.ByteSize, Checksum = a.Checksum, Description = a.Description, UploadedByUserId = a.UploadedByUserId, UploadedOn = a.UploadedOn, ScanState = a.ScanState, Classification = a.Classification, IsProtected = a.IsProtected, Data = includeData && a.Data != null ? Convert.ToBase64String(a.Data) : null };

		public static InvestigationCaseData ToCase(RmsInvestigationCase c) => c == null ? null : new InvestigationCaseData { CaseId = c.RmsInvestigationCaseId, CaseNumber = c.CaseNumber, Title = c.Title, State = c.State, OpenedOn = c.OpenedOn, OpenedByUserId = c.OpenedByUserId, LeadInvestigatorUserId = c.LeadInvestigatorUserId, OccupancyId = c.RmsOccupancyId, CallId = c.CallId, IncidentSummary = c.IncidentSummary, CauseClassification = c.CauseClassification, CauseDetail = c.CauseDetail, OriginDescription = c.OriginDescription, Findings = c.Findings, FindingsAuthorUserId = c.FindingsAuthorUserId, FindingsRecordedOn = c.FindingsRecordedOn, FindingsApprovedOn = c.FindingsApprovedOn, FindingsApprovedByUserId = c.FindingsApprovedByUserId, RecommendsIncidentAmendment = c.RecommendsIncidentAmendment, ClosedOn = c.ClosedOn, ClosedByUserId = c.ClosedByUserId, ClosureReason = c.ClosureReason, IsProtected = c.IsProtected, RowVersion = c.RowVersion };
		public static CaseMemberData ToMember(RmsInvestigationCaseMember m) => m == null ? null : new CaseMemberData { MemberId = m.RmsInvestigationCaseMemberId, UserId = m.UserId, Role = m.Role, AddedOn = m.AddedOn, RemovedOn = m.RemovedOn };
		public static CaseIncidentData ToIncident(RmsInvestigationCaseIncident i) => i == null ? null : new CaseIncidentData { LinkId = i.RmsInvestigationCaseIncidentId, RecordId = i.RecordId, PinnedRevisionId = i.PinnedRevisionId, RecordNumber = i.RecordNumber, LinkedOn = i.LinkedOn };
		public static InvestigationNoteData ToNote(RmsInvestigationNote n) => n == null ? null : new InvestigationNoteData { NoteId = n.RmsInvestigationNoteId, Kind = n.Kind, OccurredOn = n.OccurredOn, AuthorUserId = n.AuthorUserId, Subject = n.Subject, Body = n.Body, IsLocked = n.IsLocked, IsProtected = n.IsProtected };
		public static InvestigationEvidenceData ToEvidence(RmsInvestigationEvidence e) => e == null ? null : new InvestigationEvidenceData { EvidenceId = e.RmsInvestigationEvidenceId, EvidenceNumber = e.EvidenceNumber, Kind = e.Kind, Description = e.Description, CollectedOn = e.CollectedOn, CollectedByUserId = e.CollectedByUserId, CollectedFrom = e.CollectedFrom, State = e.State, CurrentCustodianUserId = e.CurrentCustodianUserId, CurrentCustodianExternal = e.CurrentCustodianExternal, StorageLocation = e.StorageLocation, IsProtected = e.IsProtected };
		public static CustodyData ToCustody(RmsInvestigationCustody c) => c == null ? null : new CustodyData { CustodyId = c.RmsInvestigationCustodyId, EvidenceId = c.RmsInvestigationEvidenceId, Sequence = c.Sequence, TransferredOn = c.TransferredOn, FromUserId = c.FromUserId, FromExternal = c.FromExternal, ToUserId = c.ToUserId, ToExternal = c.ToExternal, Reason = c.Reason, ResultingState = c.ResultingState, RecordedByUserId = c.RecordedByUserId };
		public static ReferralData ToReferral(RmsInvestigationReferral r) => r == null ? null : new ReferralData { ReferralId = r.RmsInvestigationReferralId, Agency = r.Agency, ReferredOn = r.ReferredOn, ReferredByUserId = r.ReferredByUserId, Reason = r.Reason, ReferenceNumber = r.ReferenceNumber, State = r.State };
		public static InvestigationCaseAggregateData ToCaseAggregate(InvestigationCaseAggregate a) => a == null ? null : new InvestigationCaseAggregateData
		{
			Case = ToCase(a.Case), CallerRole = a.CallerRole.HasValue ? (int)a.CallerRole.Value : (int?)null, Members = a.Members.Select(ToMember).ToList(), Incidents = a.Incidents.Select(ToIncident).ToList(), Notes = a.Notes.Select(ToNote).ToList(),
			Evidence = a.Evidence.Select(ToEvidence).ToList(), Referrals = a.Referrals.Select(ToReferral).ToList(), Attachments = a.Attachments.Select(x => ToAttachment(x, false)).ToList(),
			IsProtected = a.Protection.IsProtected, ProtectedReason = a.Protection.ProtectedReason, RedactedFields = a.Protection.RedactedFields.Distinct().ToList()
		};

		public static QualityRubricData ToRubric(RmsQualityRubric r) => r == null ? null : new QualityRubricData { RubricId = r.RmsQualityRubricId, Name = r.Name, DefinitionKey = r.DefinitionKey, SampleSize = r.SampleSize, IsActive = r.IsActive, Criteria = Resgrid.Services.Records.RecordsQualityReviewService.ParseCriteria(r.CriteriaJson) };
		public static QualityReviewData ToReview(RmsQualityReview r) => r == null ? null : new QualityReviewData { ReviewId = r.RmsQualityReviewId, RubricId = r.RmsQualityRubricId, RecordId = r.RecordId, RecordKind = r.RecordKind, RevisionId = r.RevisionId, DefinitionKey = r.DefinitionKey, RecordNumber = r.RecordNumber, AuthorUserId = r.AuthorUserId, UnitId = r.UnitId, ReviewerUserId = r.ReviewerUserId, SampledOn = r.SampledOn, ScoredOn = r.ScoredOn, Score = r.Score, Criteria = Resgrid.Services.Records.RecordsQualityReviewService.ParseCriteria(r.CriteriaJson), Findings = Resgrid.Services.Records.RecordsQualityReviewService.ParseFindings(r.FindingsJson), Note = r.Note, AmendmentRecommended = r.AmendmentRecommended, IsProtected = r.IsProtected };
	}
}
