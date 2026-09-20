using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.CostRecovery.CalOesMars
{
	// Workforce & Business Operations plan, Phase C-M3: the typed snapshots a work item carries, the readiness /
	// validation / handoff results the screens show, and the pure calculator's input and output.

	#region Readiness

	public enum CalOesMarsReadinessSeverities
	{
		Ok = 0,
		Warning = 1,
		Blocker = 2
	}

	public sealed class CalOesMarsReadinessItem
	{
		public string Key { get; set; }
		/// <summary><see cref="CalOesMarsReadinessSeverities"/>.</summary>
		public int Severity { get; set; }
		/// <summary>Localization key on the CalOesMars family.</summary>
		public string MessageKey { get; set; }
		public string Detail { get; set; }
		/// <summary>The screen that fixes it ("Agency", "Resources", "Rates", "Agreements").</summary>
		public string Area { get; set; }
	}

	public sealed class CalOesMarsReadiness
	{
		public DateTime AsOf { get; set; }
		public string AuthorityProfileCode { get; set; }
		public bool AuthorityProfileCurrent { get; set; }
		public CalOesMarsAgencyProfile Agency { get; set; }
		public List<CalOesMarsReadinessItem> Items { get; set; } = new List<CalOesMarsReadinessItem>();
		public int ResourceProfiles { get; set; }
		public int ResourceMismatches { get; set; }
		public List<CalOesMarsRateProfile> CurrentRateProfiles { get; set; } = new List<CalOesMarsRateProfile>();
		public List<CalOesMarsAgreementSnapshot> CurrentAgreements { get; set; } = new List<CalOesMarsAgreementSnapshot>();
		public int OpenWorkItems { get; set; }
		public int ReturnedWorkItems { get; set; }
		public int InvoicesAwaitingLocalApproval { get; set; }
		public bool IsReady => Items.All(i => i.Severity != (int)CalOesMarsReadinessSeverities.Blocker);
	}

	#endregion

	#region Snapshots (SnapshotJson)

	/// <summary>The prepared F-42: one per ordered resource / request (a redispatch is a new work item).</summary>
	public sealed class CalOesMarsF42Snapshot
	{
		public string AuthorityProfileCode { get; set; }
		public string MacsDesignator { get; set; }
		public string AgencyName { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string ParentRequestNumber { get; set; }
		public string ResourceKind { get; set; }
		public string ResourceType { get; set; }
		public string StrikeTeamOrTaskForce { get; set; }
		public string ReportingLocation { get; set; }
		public string PointOfHire { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? CommittedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public bool IsRedispatch { get; set; }
		public string PreviousOrderNumber { get; set; }
		public string PreviousRequestNumber { get; set; }
		public string OverheadPosition { get; set; }
		public List<CalOesMarsF42Vehicle> Vehicles { get; set; } = new List<CalOesMarsF42Vehicle>();
		public List<CalOesMarsF42Person> Personnel { get; set; } = new List<CalOesMarsF42Person>();
		public List<CalOesMarsF42Rotation> Rotations { get; set; } = new List<CalOesMarsF42Rotation>();
		public string Comments { get; set; }
		public string LossDamage { get; set; }
		public string SupplyNumbers { get; set; }
		public string RespondingSignerName { get; set; }
		public DateTime? RespondingSignedOn { get; set; }
		public string IncidentAuthorizerName { get; set; }
		public DateTime? IncidentAuthorizedOn { get; set; }
		public bool DocumentationOnly { get; set; }
		public List<int> AttachmentIds { get; set; } = new List<int>();
		/// <summary>The DTR ids that pre-filled the time facts (provenance; a DTR is not an F-42).</summary>
		public List<string> SourceTimeReportIds { get; set; } = new List<string>();
	}

	public sealed class CalOesMarsF42Vehicle
	{
		/// <summary>"Apparatus", "Support", "POV", "Equipment".</summary>
		public string Kind { get; set; }
		public string DeploymentUnitId { get; set; }
		public string DeploymentEquipmentId { get; set; }
		public string ResourceProfileId { get; set; }
		public string Designator { get; set; }
		public string ResourceCode { get; set; }
		public string LicensePlate { get; set; }
		public string Vin { get; set; }
		public string SerialNumber { get; set; }
		public decimal? StartOdometer { get; set; }
		public decimal? EndOdometer { get; set; }
		public decimal? Miles { get; set; }
		public decimal CommittedHours { get; set; }
		public decimal CommittedDays { get; set; }
		public string FemaCode { get; set; }
	}

	public sealed class CalOesMarsF42Person
	{
		public string DeploymentPersonnelId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Rank { get; set; }
		/// <summary>Salary Survey classification code.</summary>
		public string ClassificationCode { get; set; }
		public DateTime? CommittedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public decimal CommittedHours { get; set; }
		/// <summary>Per-day actual hours from the DTRs when the agreement is actual-hours.</summary>
		public List<CalOesMarsDailyHours> ActualHours { get; set; } = new List<CalOesMarsDailyHours>();
	}

	public sealed class CalOesMarsDailyHours
	{
		public DateTime Date { get; set; }
		public decimal Hours { get; set; }
		public string TimeReportId { get; set; }
	}

	public sealed class CalOesMarsF42Rotation
	{
		public DateTime On { get; set; }
		public string OutgoingUserId { get; set; }
		public string IncomingUserId { get; set; }
		public int? ApprovalAttachmentId { get; set; }
	}

	/// <summary>The prepared expense claim linked to a resource / F-42.</summary>
	public sealed class CalOesMarsExpenseClaimSnapshot
	{
		public string AuthorityProfileCode { get; set; }
		public string F42WorkItemId { get; set; }
		public string RequestNumber { get; set; }
		public string IncidentNumber { get; set; }
		/// <summary>The portal's unmatched / travel-only path when no F-42 exists for the resource.</summary>
		public bool TravelOnly { get; set; }
		public List<CalOesMarsExpenseLine> Lines { get; set; } = new List<CalOesMarsExpenseLine>();
		public string SignerName { get; set; }
		public DateTime? SignedOn { get; set; }
		public string ApproverName { get; set; }
		public DateTime? ApprovedOn { get; set; }
	}

	public sealed class CalOesMarsExpenseLine
	{
		public string DeploymentExpenseId { get; set; }
		public DateTime Date { get; set; }
		public string City { get; set; }
		/// <summary>"Meal", "Lodging", "Miscellaneous".</summary>
		public string Category { get; set; }
		public decimal Amount { get; set; }
		public string Description { get; set; }
		public int? ReceiptAttachmentId { get; set; }
		public bool PreApproved { get; set; }
	}

	/// <summary>The observed MARS-generated invoice.</summary>
	public sealed class CalOesMarsInvoiceSnapshot
	{
		public string MarsInvoiceId { get; set; }
		public DateTime? InvoiceDate { get; set; }
		public decimal? InvoicedTotal { get; set; }
		public string PayingEntity { get; set; }
		public List<string> CoveredWorkItemIds { get; set; } = new List<string>();
		public int? InvoiceAttachmentId { get; set; }
		public string LocalDecisionComment { get; set; }
		public string LocalDecisionTitle { get; set; }
	}

	#endregion

	#region Validation and handoff

	public sealed class CalOesMarsValidationIssue
	{
		public string Box { get; set; }
		public string Code { get; set; }
		public string Detail { get; set; }
	}

	public sealed class CalOesMarsValidationResult
	{
		public string WorkItemId { get; set; }
		public string AuthorityProfileCode { get; set; }
		public DateTime ValidatedOn { get; set; }
		public List<CalOesMarsValidationIssue> Errors { get; set; } = new List<CalOesMarsValidationIssue>();
		public List<CalOesMarsValidationIssue> Warnings { get; set; } = new List<CalOesMarsValidationIssue>();
		public bool IsReadyForPortal => Errors.Count == 0;
	}

	public sealed class CalOesMarsHandoffField
	{
		public string Box { get; set; }
		public string Label { get; set; }
		public string Value { get; set; }
		public string Source { get; set; }
	}

	/// <summary>A no-store, side-by-side copy view for the portal. Opening it never marks anything submitted.</summary>
	public sealed class CalOesMarsHandoffManifest
	{
		public string WorkItemId { get; set; }
		public int RecordType { get; set; }
		public string AuthorityProfileCode { get; set; }
		public string RateProfileVersion { get; set; }
		public string AgreementSnapshotId { get; set; }
		public DateTime GeneratedOn { get; set; }
		public string GeneratedByUserId { get; set; }
		public string Checksum { get; set; }
		public string PortalUrl { get; set; }
		public List<CalOesMarsHandoffField> Fields { get; set; } = new List<CalOesMarsHandoffField>();
		public List<string> SupportingAttachmentNames { get; set; } = new List<string>();
		public CalOesMarsValidationResult Validation { get; set; }
		/// <summary>Always true in P0: the packet is evidence, not an accepted MARS import file.</summary>
		public bool NotAnImportFile => true;
	}

	/// <summary>An observed external fact recorded by a MARS manager (P0: manual observation only).</summary>
	public sealed class CalOesMarsExternalObservation
	{
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public string Source { get; set; } = CalOesMarsObservationSources.Manual;
		public string Comment { get; set; }
		public string ArtifactChecksum { get; set; }
		public int? ArtifactAttachmentId { get; set; }
	}

	public sealed class CalOesMarsInvoiceObservation
	{
		public string MarsInvoiceId { get; set; }
		public DateTime? InvoiceDate { get; set; }
		public decimal InvoicedTotal { get; set; }
		public string PayingEntity { get; set; }
		public string ExternalStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public List<string> CoveredWorkItemIds { get; set; } = new List<string>();
		public int? InvoiceAttachmentId { get; set; }
		public string Comment { get; set; }
	}

	public sealed class CalOesMarsPaymentObservation
	{
		public decimal PaidTotal { get; set; }
		public DateTime PaidOn { get; set; }
		public string PaymentReference { get; set; }
		public string PayingEntityStatus { get; set; }
		public DateTime? ObservedOn { get; set; }
		public string Comment { get; set; }
	}

	#endregion

	#region Calculator

	/// <summary>Everything the pure calculator needs; the service assembles it from the snapshot, the effective rate profiles and the agreement.</summary>
	public sealed class CalOesMarsReimbursementInput
	{
		public CalOesMarsF42Snapshot F42 { get; set; }
		public CalOesMarsExpenseClaimSnapshot Expenses { get; set; }
		public List<CalOesMarsRateLine> RateLines { get; set; } = new List<CalOesMarsRateLine>();
		public CalOesMarsAgreementSnapshot Agreement { get; set; }
		/// <summary>Administrative rate percent applied to eligible personnel reimbursement; null = no administrative line.</summary>
		public decimal? AdministrativeRatePercent { get; set; }
		public string RateProfileVersion { get; set; }
	}

	public sealed class CalOesMarsReimbursementResult
	{
		public List<CalOesMarsReimbursementLine> Lines { get; set; } = new List<CalOesMarsReimbursementLine>();
		public List<CalOesMarsValidationIssue> Exceptions { get; set; } = new List<CalOesMarsValidationIssue>();
		public decimal ExpectedTotal => Lines.Where(l => l.EligibilityState == (int)CalOesMarsEligibilityStates.Eligible).Sum(l => l.ExpectedAmount);
		public decimal UncertainTotal => Lines.Where(l => l.EligibilityState == (int)CalOesMarsEligibilityStates.Uncertain).Sum(l => l.ExpectedAmount);
	}

	public static class CalOesMarsExceptionCodes
	{
		public const string NoAgreement = "no_agreement";
		public const string NoSalaryRate = "no_salary_rate";
		public const string NoApparatusRate = "no_apparatus_rate";
		public const string NoSupportRate = "no_support_rate";
		public const string NoPovRate = "no_pov_rate";
		public const string NoSpecialEquipmentRate = "no_special_equipment_rate";
		public const string NoAdministrativeRate = "no_administrative_rate";
		public const string OvertimePerAgreement = "overtime_per_agreement";
		public const string NoActualHours = "no_actual_hours";
		public const string ExpenseWithoutReceipt = "expense_without_receipt";
		public const string ExpenseNotPreApproved = "expense_not_pre_approved";
		public const string MileageWithoutOdometer = "mileage_without_odometer";
	}

	public static class CalOesMarsValidationCodes
	{
		public const string AuthorityProfileMissing = "authority_profile_missing";
		public const string AgencyProfileMissing = "agency_profile_missing";
		public const string MacsMissing = "macs_missing";
		public const string IncidentMissing = "incident_missing";
		public const string OrderMissing = "order_missing";
		public const string RequestMissing = "request_missing";
		public const string RequestPrefixInvalid = "request_prefix_invalid";
		public const string ResourceMissing = "resource_missing";
		public const string DispatchMissing = "dispatch_missing";
		public const string ReturnBeforeDispatch = "return_before_dispatch";
		public const string ReleaseIsNotReturn = "release_is_not_return";
		public const string PersonnelMissing = "personnel_missing";
		public const string PersonnelIntervalOutside = "personnel_interval_outside";
		public const string DuplicateVehicle = "duplicate_vehicle";
		public const string VehicleNotInInventory = "vehicle_not_in_inventory";
		public const string RotationUndocumented = "rotation_undocumented";
		public const string RespondingSignatureMissing = "responding_signature_missing";
		public const string IncidentAuthorizationMissing = "incident_authorization_missing";
		public const string PaperFallbackMissing = "paper_fallback_missing";
		public const string AgreementMissing = "agreement_missing";
		public const string RateProfileMissing = "rate_profile_missing";
		public const string ClassificationUnmapped = "classification_unmapped";
		public const string ExpenseNoLines = "expense_no_lines";
		public const string ExpenseReceiptMissing = "expense_receipt_missing";
		public const string ExpenseF42NotSubmitted = "expense_f42_not_submitted";
		public const string ExpenseSignatureMissing = "expense_signature_missing";
		public const string ExpenseApprovalMissing = "expense_approval_missing";
		public const string DocumentationOnly = "documentation_only";
	}

	#endregion

	#region Queue

	public sealed class CalOesMarsQueueItem
	{
		public CalOesMarsWorkItem WorkItem { get; set; }
		public string DeploymentName { get; set; }
		public string IncidentNumber { get; set; }
		public string RequestNumber { get; set; }
		public int ErrorCount { get; set; }
		public int WarningCount { get; set; }
		public int AgeDays { get; set; }
		public bool IsMine { get; set; }
	}

	#endregion
}
