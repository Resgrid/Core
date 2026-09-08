using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Lifecycle of an inspection (RMS plan section 4.3, RMS-5).</summary>
	public enum RmsInspectionState
	{
		Scheduled = 1,
		InProgress = 2,
		Completed = 3,
		/// <summary>Completed with open violations; a child re-inspection is expected by the violation due date.</summary>
		ReinspectionRequired = 4,
		Closed = 5,
		Cancelled = 6
	}

	public enum RmsInspectionResult
	{
		NotRecorded = 0,
		Pass = 1,
		Fail = 2,
		Conditional = 3
	}

	public enum RmsViolationState
	{
		Open = 1,
		Corrected = 2,
		Verified = 3,
		Escalated = 4,
		Waived = 5
	}

	public enum RmsViolationSeverity
	{
		Minor = 1,
		Moderate = 2,
		Serious = 3,
		/// <summary>Imminent hazard; immediate correction or evacuation.</summary>
		Critical = 4
	}

	public enum RmsHydrantType
	{
		DryBarrel = 1,
		WetBarrel = 2,
		Standpipe = 3,
		Cistern = 4,
		DraftingSite = 5,
		Other = 6
	}

	public enum RmsHydrantOwnerKind
	{
		Municipal = 1,
		Private = 2,
		Department = 3,
		Other = 4
	}

	/// <summary>NFPA 291 flow classification (colour of the bonnet/caps).</summary>
	public enum RmsHydrantFlowClass
	{
		Unknown = 0,
		/// <summary>1500 gpm or more — light blue.</summary>
		AA = 1,
		/// <summary>1000–1499 gpm — green.</summary>
		A = 2,
		/// <summary>500–999 gpm — orange.</summary>
		B = 3,
		/// <summary>Under 500 gpm — red.</summary>
		C = 4
	}

	public enum RmsHydrantMaintenanceKind
	{
		Inspection = 1,
		Flush = 2,
		Repair = 3,
		Paint = 4,
		Winterize = 5,
		Other = 6
	}

	public enum RmsPermitState
	{
		Applied = 1,
		UnderReview = 2,
		Approved = 3,
		Issued = 4,
		Expired = 5,
		Revoked = 6,
		Denied = 7,
		Closed = 8
	}

	public enum RmsPlanReviewOutcome
	{
		Pending = 0,
		Approved = 1,
		CorrectionsRequired = 2,
		Rejected = 3
	}

	public enum RmsCrrActivityKind
	{
		PublicEducation = 1,
		SmokeAlarmInstallation = 2,
		HomeSafetyVisit = 3,
		PublicEvent = 4,
		SchoolProgram = 5,
		MediaOutreach = 6,
		Other = 7
	}

	/// <summary>Parent aggregate of a prevention attachment.</summary>
	public enum RmsPreventionParentKind
	{
		Occupancy = 1,
		Inspection = 2,
		Violation = 3,
		Hydrant = 4,
		Permit = 5,
		CrrActivity = 6,
		/// <summary>Investigation photos and documents; always restricted.</summary>
		InvestigationCase = 7,
		InvestigationEvidence = 8
	}

	/// <summary>Sequence names used by <see cref="RmsPreventionSequence"/>.</summary>
	public static class RmsPreventionNumberKinds
	{
		public const string Occupancy = "OCC";
		public const string Inspection = "INSP";
		public const string Permit = "PRM";
		public const string Investigation = "INV";
		public const string Evidence = "EV";
	}

	/// <summary>An adopted code set (e.g. IFC 2021 with local amendments).</summary>
	public class RmsCodeSet : IEntity
	{
		public string RmsCodeSetId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string Name { get; set; }
		public string Edition { get; set; }
		public string Jurisdiction { get; set; }
		public bool IsActive { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsCodeSetId; set => RmsCodeSetId = (string)value; }
		public string TableName => "RmsCodeSets";
		public string IdName => "RmsCodeSetId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One citable section of a code set.</summary>
	public class RmsCodeSection : IEntity
	{
		public string RmsCodeSectionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsCodeSetId { get; set; }
		public string SectionNumber { get; set; }
		public string Title { get; set; }
		public string Text { get; set; }
		/// <summary><see cref="RmsViolationSeverity"/> proposed when a violation cites this section.</summary>
		public int DefaultSeverity { get; set; }
		/// <summary>Default days to correct a violation citing this section.</summary>
		public int DefaultCorrectionDays { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsCodeSectionId; set => RmsCodeSectionId = (string)value; }
		public string TableName => "RmsCodeSections";
		public string IdName => "RmsCodeSectionId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An inspection program: which occupancy types it applies to, how often, and the checklist an inspector walks.
	/// The checklist is bounded JSON (<see cref="RmsInspectionChecklistItem"/> list) rather than an RMS definition
	/// because an inspection item is pass/fail against a code citation, not a typed field.
	/// </summary>
	public class RmsInspectionProgram : IEntity
	{
		public string RmsInspectionProgramId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		/// <summary>Contacts plan OccupancyTypes values this program applies to, comma separated; empty = all.</summary>
		public string OccupancyTypesCsv { get; set; }
		/// <summary>Months between inspections; 0 = on demand only.</summary>
		public int FrequencyMonths { get; set; }
		public string RmsCodeSetId { get; set; }
		public string ChecklistJson { get; set; }
		public bool IsActive { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsInspectionProgramId; set => RmsInspectionProgramId = (string)value; }
		public string TableName => "RmsInspectionPrograms";
		public string IdName => "RmsInspectionProgramId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One item of an inspection program checklist (stored in ChecklistJson).</summary>
	public class RmsInspectionChecklistItem
	{
		public string Key { get; set; }
		public string Text { get; set; }
		public string RmsCodeSectionId { get; set; }
		public bool Required { get; set; }
		public int Order { get; set; }
	}

	/// <summary>The inspector's answer to one checklist item (stored in RmsInspection.ItemsJson).</summary>
	public class RmsInspectionItemResult
	{
		public string Key { get; set; }
		/// <summary>true = compliant, false = violation, null = not inspected.</summary>
		public bool? Passed { get; set; }
		public string Note { get; set; }
	}

	public class RmsInspection : IEntity
	{
		public string RmsInspectionId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsOccupancyId { get; set; }
		public string RmsInspectionProgramId { get; set; }
		/// <summary>INSP-{year}-{sequence}.</summary>
		public string InspectionNumber { get; set; }
		/// <summary><see cref="RmsInspectionState"/>.</summary>
		public int State { get; set; }
		/// <summary><see cref="RmsInspectionResult"/>.</summary>
		public int Result { get; set; }
		public DateTime? ScheduledOn { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string InspectorUserId { get; set; }
		public string ItemsJson { get; set; }
		public string Notes { get; set; }
		/// <summary>Printed name of the occupant representative who signed the inspection, if any.</summary>
		public string SignatureName { get; set; }
		public DateTime? SignedOn { get; set; }
		/// <summary>The inspection this one re-checks.</summary>
		public string ParentInspectionId { get; set; }
		public DateTime? NoticeIssuedOn { get; set; }
		public string NoticeReference { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsInspectionId; set => RmsInspectionId = (string)value; }
		public string TableName => "RmsInspections";
		public string IdName => "RmsInspectionId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsViolation : IEntity
	{
		public string RmsViolationId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInspectionId { get; set; }
		public string RmsOccupancyId { get; set; }
		public string RmsCodeSetId { get; set; }
		public string RmsCodeSectionId { get; set; }
		public string ChecklistItemKey { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="RmsViolationSeverity"/>.</summary>
		public int Severity { get; set; }
		public string CorrectiveAction { get; set; }
		public DateTime? DueOn { get; set; }
		/// <summary><see cref="RmsViolationState"/>.</summary>
		public int State { get; set; }
		public DateTime? CorrectedOn { get; set; }
		public DateTime? VerifiedOn { get; set; }
		public string VerifiedByUserId { get; set; }
		public string ReinspectionId { get; set; }
		/// <summary>Set once the overdue event has been raised, so the sweep emits it exactly once (same rule as record due states).</summary>
		public DateTime? OverdueEmittedOn { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public bool IsOpen => State == (int)RmsViolationState.Open || State == (int)RmsViolationState.Escalated;

		public object IdValue { get => RmsViolationId; set => RmsViolationId = (string)value; }
		public string TableName => "RmsViolations";
		public string IdName => "RmsViolationId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	public class RmsHydrant : IEntity
	{
		public string RmsHydrantId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string HydrantNumber { get; set; }
		/// <summary><see cref="RmsHydrantType"/>.</summary>
		public int Type { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string AddressText { get; set; }
		/// <summary><see cref="RmsHydrantOwnerKind"/>.</summary>
		public int OwnerKind { get; set; }
		public string OwnerName { get; set; }
		public decimal? MainSizeInches { get; set; }
		public int? StaticPressurePsi { get; set; }
		public int? ResidualPressurePsi { get; set; }
		public int? FlowGpm { get; set; }
		/// <summary><see cref="RmsHydrantFlowClass"/>.</summary>
		public int FlowClass { get; set; }
		public bool InService { get; set; }
		public string OutOfServiceReason { get; set; }
		public DateTime? OutOfServiceSince { get; set; }
		public DateTime? LastTestedOn { get; set; }
		public DateTime? LastMaintainedOn { get; set; }
		public string Notes { get; set; }
		public int? PoiId { get; set; }
		/// <summary>Where the row came from: manual, csv-import, poi.</summary>
		public string Source { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsHydrantId; set => RmsHydrantId = (string)value; }
		public string TableName => "RmsHydrants";
		public string IdName => "RmsHydrantId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsHydrantFlowTest : IEntity
	{
		public string RmsHydrantFlowTestId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsHydrantId { get; set; }
		public DateTime TestedOn { get; set; }
		public string TestedByUserId { get; set; }
		public int StaticPressurePsi { get; set; }
		public int ResidualPressurePsi { get; set; }
		public int PitotPressurePsi { get; set; }
		public decimal OutletDiameterInches { get; set; }
		/// <summary>Discharge coefficient of the outlet (0.9 smooth, 0.8 square, 0.7 projecting).</summary>
		public decimal Coefficient { get; set; }
		/// <summary>Computed: 29.83 × c × d² × √p.</summary>
		public int FlowGpm { get; set; }
		/// <summary><see cref="RmsHydrantFlowClass"/> derived from FlowGpm.</summary>
		public int FlowClass { get; set; }
		public string Notes { get; set; }
		public DateTime CreatedOn { get; set; }

		public object IdValue { get => RmsHydrantFlowTestId; set => RmsHydrantFlowTestId = (string)value; }
		public string TableName => "RmsHydrantFlowTests";
		public string IdName => "RmsHydrantFlowTestId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsHydrantMaintenance : IEntity
	{
		public string RmsHydrantMaintenanceId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsHydrantId { get; set; }
		public DateTime PerformedOn { get; set; }
		public string PerformedByUserId { get; set; }
		/// <summary><see cref="RmsHydrantMaintenanceKind"/>.</summary>
		public int Kind { get; set; }
		public string Notes { get; set; }
		public bool ReturnedToService { get; set; }
		public DateTime CreatedOn { get; set; }

		public object IdValue { get => RmsHydrantMaintenanceId; set => RmsHydrantMaintenanceId = (string)value; }
		public string TableName => "RmsHydrantMaintenances";
		public string IdName => "RmsHydrantMaintenanceId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsPermitType : IEntity
	{
		public string RmsPermitTypeId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string Description { get; set; }
		public int DefaultValidityDays { get; set; }
		public bool RequiresPlanReview { get; set; }
		public decimal? FeeAmount { get; set; }
		/// <summary>Default conditions pre-filled on a new permit, one per line.</summary>
		public string ConditionsTemplate { get; set; }
		public bool IsActive { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsPermitTypeId; set => RmsPermitTypeId = (string)value; }
		public string TableName => "RmsPermitTypes";
		public string IdName => "RmsPermitTypeId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsPermit : IEntity
	{
		public string RmsPermitId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsPermitTypeId { get; set; }
		public string RmsOccupancyId { get; set; }
		/// <summary>PRM-{year}-{sequence}.</summary>
		public string PermitNumber { get; set; }
		public string ApplicantContactId { get; set; }
		public string ApplicantName { get; set; }
		public string ApplicantPhone { get; set; }
		public string ApplicantEmail { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="RmsPermitState"/>.</summary>
		public int State { get; set; }
		public DateTime AppliedOn { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }
		public DateTime? IssuedOn { get; set; }
		public string IssuedByUserId { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>Conditions of issue, one per line.</summary>
		public string Conditions { get; set; }
		public string ReviewNotes { get; set; }
		public decimal? FeeAmount { get; set; }
		public DateTime? FeePaidOn { get; set; }
		/// <summary>Optional reference into the invoicing system (Contacts plan Phase B) — text only, never a join.</summary>
		public string InvoiceReference { get; set; }
		public string DecisionReason { get; set; }
		/// <summary>Set once the expiring-soon event has been raised, so the sweep emits it exactly once.</summary>
		public DateTime? ExpiringEmittedOn { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsPermitId; set => RmsPermitId = (string)value; }
		public string TableName => "RmsPermits";
		public string IdName => "RmsPermitId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsPlanReview : IEntity
	{
		public string RmsPlanReviewId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsPermitId { get; set; }
		public int CycleNumber { get; set; }
		public DateTime SubmittedOn { get; set; }
		public string ReviewerUserId { get; set; }
		public DateTime? ReviewedOn { get; set; }
		/// <summary><see cref="RmsPlanReviewOutcome"/>.</summary>
		public int Outcome { get; set; }
		public string Comments { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsPlanReviewId; set => RmsPlanReviewId = (string)value; }
		public string TableName => "RmsPlanReviews";
		public string IdName => "RmsPlanReviewId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A community risk reduction activity (plan section 4.3). Carries no personal information about attendees, only counts.</summary>
	public class RmsCrrActivity : IEntity
	{
		public string RmsCrrActivityId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary><see cref="RmsCrrActivityKind"/>.</summary>
		public int Kind { get; set; }
		public DateTime OccurredOn { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string RmsOccupancyId { get; set; }
		public string LocationText { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public int AudienceCount { get; set; }
		public int SmokeAlarmsInstalled { get; set; }
		public decimal HoursSpent { get; set; }
		/// <summary>Department members who staffed the activity, comma separated user ids.</summary>
		public string StaffUserIdsCsv { get; set; }
		public string Outcome { get; set; }
		/// <summary>Reserved for the NERIS CRR secondary schema once it matures; JSON, additive only.</summary>
		public string NerisSecondaryJson { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsCrrActivityId; set => RmsCrrActivityId = (string)value; }
		public string TableName => "RmsCrrActivities";
		public string IdName => "RmsCrrActivityId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A file on a prevention or investigation aggregate. Goes through the same hygiene and scanner as record
	/// attachments; the name, description and bytes are ADP catalog v13 fields. Investigation attachments are
	/// restricted and readable only by case members.
	/// </summary>
	public class RmsPreventionAttachment : IEntity
	{
		public string RmsPreventionAttachmentId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary><see cref="RmsPreventionParentKind"/>.</summary>
		public int ParentKind { get; set; }
		public string ParentId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ByteSize { get; set; }
		public string Checksum { get; set; }
		public byte[] Data { get; set; }
		public string Description { get; set; }
		public string UploadedByUserId { get; set; }
		public DateTime UploadedOn { get; set; }
		/// <summary><see cref="RmsAttachmentScanState"/>.</summary>
		public int ScanState { get; set; }
		public bool MetadataStripped { get; set; }
		/// <summary><see cref="RmsEvidenceClassification"/>: Restricted for every investigation attachment.</summary>
		public int Classification { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsPreventionAttachmentId; set => RmsPreventionAttachmentId = (string)value; }
		public string TableName => "RmsPreventionAttachments";
		public string IdName => "RmsPreventionAttachmentId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-department, per-kind, per-year number sequence for the prevention aggregates (OCC/INSP/PRM/INV/EV).</summary>
	public class RmsPreventionSequence : IEntity
	{
		public string RmsPreventionSequenceId { get; set; }
		public int DepartmentId { get; set; }
		public string Kind { get; set; }
		public int Year { get; set; }
		public int LastValue { get; set; }
		public DateTime ModifiedOn { get; set; }

		public object IdValue { get => RmsPreventionSequenceId; set => RmsPreventionSequenceId = (string)value; }
		public string TableName => "RmsPreventionSequences";
		public string IdName => "RmsPreventionSequenceId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>NFPA 291 hydrant flow arithmetic, pure so it is unit-testable and shared by web, API and import.</summary>
	public static class HydrantFlowCalculator
	{
		/// <summary>Q = 29.83 × c × d² × √p (gpm), rounded to the nearest 10 as the standard reports.</summary>
		public static int FlowGpm(decimal coefficient, decimal outletDiameterInches, int pitotPsi)
		{
			if (coefficient <= 0 || outletDiameterInches <= 0 || pitotPsi <= 0)
				return 0;
			var q = 29.83 * (double)coefficient * Math.Pow((double)outletDiameterInches, 2) * Math.Sqrt(pitotPsi);
			return (int)(Math.Round(q / 10.0) * 10);
		}

		public static RmsHydrantFlowClass Classify(int? flowGpm)
		{
			if (!flowGpm.HasValue || flowGpm.Value <= 0) return RmsHydrantFlowClass.Unknown;
			if (flowGpm.Value >= 1500) return RmsHydrantFlowClass.AA;
			if (flowGpm.Value >= 1000) return RmsHydrantFlowClass.A;
			if (flowGpm.Value >= 500) return RmsHydrantFlowClass.B;
			return RmsHydrantFlowClass.C;
		}
	}
}
