using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	public class IncidentReportsResult : StandardApiResponseV4Base
	{
		public List<IncidentReportSummaryData> Data { get; set; } = new List<IncidentReportSummaryData>();
		public int Total { get; set; }
	}

	public class IncidentReportSummaryData
	{
		public string ReportId { get; set; }
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public int CallId { get; set; }
		public string IncidentNumber { get; set; }
		public string NerisIncidentId { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public int? LastSubmissionState { get; set; }
		public string LastSubmissionStateName { get; set; }
		public string DisplaySummary { get; set; }
		public int? StationGroupId { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime? CallCreatedOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public long RowVersion { get; set; }
	}

	public class IncidentReportResult : StandardApiResponseV4Base
	{
		public IncidentReportData Data { get; set; }
	}

	/// <summary>A hydrated NERIS incident report: header, dispatch facts, sections, provenance, validation issues, sanitized submission history.</summary>
	public class IncidentReportData
	{
		public string ReportId { get; set; }
		public int CallId { get; set; }
		public string ReportingEntityId { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public string ProfileVersion { get; set; }
		public int LifecyclePreset { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public string DisplaySummary { get; set; }
		public string IncidentNumber { get; set; }
		public string NerisIncidentId { get; set; }
		public string LastSubmissionId { get; set; }
		public int? LastSubmissionState { get; set; }
		public string LastSubmissionStateName { get; set; }
		public DateTime? LastSubmittedOn { get; set; }
		public DateTime? AcceptedOn { get; set; }
		public DateTime? RejectedOn { get; set; }
		public string RejectionSummary { get; set; }
		public int? StationGroupId { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public string ReviewerUserId { get; set; }
		public DateTime? CallCreatedOn { get; set; }
		public DateTime? CallAnsweredOn { get; set; }
		public DateTime? CallArrivalOn { get; set; }
		public DateTime? IncidentClearedOn { get; set; }
		public string DispatchCenterId { get; set; }
		public string DeterminantCode { get; set; }
		public string DispatchIncidentCode { get; set; }
		public string Disposition { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public int? AnimalsRescued { get; set; }
		public List<string> SpecialModifiers { get; set; } = new List<string>();
		public DateTime? ReviewDueOn { get; set; }
		public DateTime? SubmittedForReviewOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public string ReturnReasonCode { get; set; }
		public string ReturnReasonText { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public string CurrentRevisionId { get; set; }
		public int RevisionCount { get; set; }
		public string AmendsRevisionId { get; set; }
		public DateTime? VoidedOn { get; set; }
		public string VoidReasonCode { get; set; }
		public DateTime? CancelledOn { get; set; }
		public int OriginClient { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
		public bool IsEditable { get; set; }
		public bool SubmissionEnabled { get; set; }
		public bool CanQueueSubmission { get; set; }
		public bool HasBlockingIssues { get; set; }
		public List<string> AvailableTransitions { get; set; } = new List<string>();
		public IncidentLocationData Location { get; set; }
		public List<IncidentTypeData> Types { get; set; } = new List<IncidentTypeData>();
		public List<IncidentUnitResponseData> Units { get; set; } = new List<IncidentUnitResponseData>();
		public List<IncidentAidData> Aids { get; set; } = new List<IncidentAidData>();
		public List<IncidentTacticData> Tactics { get; set; } = new List<IncidentTacticData>();
		public string Narrative { get; set; }
		public string ImpedimentNarrative { get; set; }
		public string OutcomeNarrative { get; set; }
		public string SupplementalJson { get; set; }
		public List<IncidentFactData> Facts { get; set; } = new List<IncidentFactData>();
		// RMS-3 conditional sections. Sections lists what the selected incident types demand, so a client renders
		// the same progressive rules the server validates against instead of hard-coding its own copy.
		public List<IncidentSectionRequirementData> Sections { get; set; } = new List<IncidentSectionRequirementData>();
		public List<IncidentModuleData> Modules { get; set; } = new List<IncidentModuleData>();
		public List<IncidentResourceData> Resources { get; set; } = new List<IncidentResourceData>();
		public List<IncidentCasualtyData> Casualties { get; set; } = new List<IncidentCasualtyData>();
		public List<IncidentExposureData> Exposures { get; set; } = new List<IncidentExposureData>();
		/// <summary>Restricted fields withheld from this response because the caller lacks RecordRestricted_View.</summary>
		public List<string> WithheldFields { get; set; } = new List<string>();
		/// <summary>The analysis filing for this incident, when one has been started.</summary>
		public string IncidentAnalysisId { get; set; }
		public List<IncidentIssueData> Issues { get; set; } = new List<IncidentIssueData>();
		public List<IncidentSubmissionData> Submissions { get; set; } = new List<IncidentSubmissionData>();
		public List<IncidentSignatureData> Signatures { get; set; } = new List<IncidentSignatureData>();
		public List<RecordRevisionData> Revisions { get; set; } = new List<RecordRevisionData>();
		public List<int> GroupScopeIds { get; set; } = new List<int>();
	}

	public class IncidentLocationData
	{
		public string AddressText { get; set; }
		public string Number { get; set; }
		public string NumberPrefix { get; set; }
		public string NumberSuffix { get; set; }
		public string Street { get; set; }
		public string UnitValue { get; set; }
		public string Municipality { get; set; }
		public string County { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public string PlaceType { get; set; }
		public string LocationUse { get; set; }
		public string CrossStreet1 { get; set; }
		public string CrossStreet2 { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string Jurisdiction { get; set; }
		public int SourceKind { get; set; }
		public string SourceKindName { get; set; }
	}

	public class IncidentTypeData
	{
		public string TypeCode { get; set; }
		public bool IsPrimary { get; set; }
		public string LocalCode { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentUnitResponseData
	{
		public int? UnitId { get; set; }
		public string UnitName { get; set; }
		public string UnitType { get; set; }
		public int? StationGroupId { get; set; }
		public string UnitNerisId { get; set; }
		public int? Staffing { get; set; }
		public bool UnableToDispatch { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? StagingOn { get; set; }
		public DateTime? CanceledEnrouteOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		public string ResponseMode { get; set; }
		public string TransportMode { get; set; }
		public int TimesSourceKind { get; set; }
		public string TimesSourceKindName { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentAidData
	{
		public string Direction { get; set; }
		public string AidType { get; set; }
		public string CounterpartNerisId { get; set; }
		public string CounterpartName { get; set; }
		public bool IsNonFireDepartment { get; set; }
		public string NonFdType { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentTacticData
	{
		public string TacticCode { get; set; }
		public int? ActorUnitId { get; set; }
		public DateTime? OccurredOn { get; set; }
		public int Ordinal { get; set; }
	}

	/// <summary>Provenance for one prefilled value (plan 4.2): where it came from, what it was, what it is now.</summary>
	public class IncidentFactData
	{
		public string FactKey { get; set; }
		public int SourceKind { get; set; }
		public string SourceKindName { get; set; }
		public string SourceSystem { get; set; }
		public string SourceEntityType { get; set; }
		public string SourceEntityId { get; set; }
		public string SourceValue { get; set; }
		public string CurrentValue { get; set; }
		public DateTime? SourceTime { get; set; }
		public DateTime? CorrectedOn { get; set; }
		public string CorrectedByUserId { get; set; }
	}

	public class IncidentIssueData
	{
		public string RuleKey { get; set; }
		public int Severity { get; set; }
		public string SeverityName { get; set; }
		public string FieldPath { get; set; }
		public string Message { get; set; }
		public int Source { get; set; }
		public string SourceName { get; set; }
	}

	/// <summary>Sanitized delivery outcome (plan 5.6): state, external id/status, attempts, codes and field paths. Never payload or response bodies.</summary>
	public class IncidentSubmissionData
	{
		public string SubmissionId { get; set; }
		public string RevisionId { get; set; }
		public string Destination { get; set; }
		public string DestinationVersion { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		public int Attempts { get; set; }
		public int MaxAttempts { get; set; }
		public string ErrorSummary { get; set; }
		public string PayloadChecksum { get; set; }
		public DateTime QueuedOn { get; set; }
		public DateTime? SentOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime? NextAttemptOn { get; set; }
	}

	public class IncidentSignatureData
	{
		public string SignatureId { get; set; }
		public string RevisionId { get; set; }
		public string SignerUserId { get; set; }
		public string SignerName { get; set; }
		public string SignerRole { get; set; }
		public int Intent { get; set; }
		public string StatementVersion { get; set; }
		public string StatementText { get; set; }
		public DateTime SignedOn { get; set; }
		public string ArtifactChecksum { get; set; }
	}

	public class IncidentValidationResult : StandardApiResponseV4Base
	{
		public List<IncidentIssueData> Data { get; set; } = new List<IncidentIssueData>();
		public bool HasBlockingIssues { get; set; }
	}

	/// <summary>Start (or return) the one authoritative incident report for a Call.</summary>
	public class StartIncidentReportInput
	{
		public int CallId { get; set; }
		public int? OriginClient { get; set; }
	}

	/// <summary>Draft save; every list replaces the draft rows. Dates are UTC.</summary>
	public class SaveIncidentReportDraftInput
	{
		public Resgrid.Model.RecordUdfInput CustomFields { get; set; }
		public string ReportId { get; set; }
		public long? RowVersion { get; set; }
		public int? OriginClient { get; set; }
		public string IncidentNumber { get; set; }
		public DateTime? CallCreatedOn { get; set; }
		public DateTime? CallAnsweredOn { get; set; }
		public DateTime? CallArrivalOn { get; set; }
		public DateTime? IncidentClearedOn { get; set; }
		public string DispatchCenterId { get; set; }
		public string DeterminantCode { get; set; }
		public string DispatchIncidentCode { get; set; }
		public string Disposition { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public int? AnimalsRescued { get; set; }
		public List<string> SpecialModifiers { get; set; } = new List<string>();
		public int? StationGroupId { get; set; }
		public IncidentLocationInput Location { get; set; }
		public List<IncidentTypeInput> Types { get; set; } = new List<IncidentTypeInput>();
		public List<IncidentUnitResponseInput> Units { get; set; } = new List<IncidentUnitResponseInput>();
		public List<IncidentAidInput> Aids { get; set; } = new List<IncidentAidInput>();
		public List<IncidentTacticInput> Tactics { get; set; } = new List<IncidentTacticInput>();
		public string Narrative { get; set; }
		public string ImpedimentNarrative { get; set; }
		public string OutcomeNarrative { get; set; }
		public string SupplementalJson { get; set; }

		// RMS-3 conditional sections. Null means "leave this section alone"; an empty list clears it. A client that
		// cannot render a section must not delete what an officer authored on the Web, which is why absence and
		// emptiness differ here and nowhere else in this input.
		public List<IncidentModuleInputData> Modules { get; set; }
		public List<IncidentResourceInputData> Resources { get; set; }
		public List<IncidentCasualtyInputData> Casualties { get; set; }
		public List<IncidentExposureInputData> Exposures { get; set; }
	}

	public class IncidentReportCommandInput
	{
		public string ReportId { get; set; }
		public long? RowVersion { get; set; }
		public string IdempotencyKey { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		public bool Attested { get; set; }
		/// <summary>Validate: also call the destination's validate endpoint when submission is enabled.</summary>
		public bool IncludeDestination { get; set; } = true;
		public int? OriginClient { get; set; }
	}

	/// <summary>One conditional section the selected incident types demand or suggest (plan section 4.2).</summary>
	public class IncidentSectionRequirementData
	{
		public int Kind { get; set; }
		public string KindName { get; set; }
		/// <summary>Dotted path in the pinned NERIS contract this section serializes into.</summary>
		public string PayloadPath { get; set; }
		public string SchemaName { get; set; }
		public bool IsCollection { get; set; }
		/// <summary>True blocks finalization; false is a warning the author may answer or ignore.</summary>
		public bool Required { get; set; }
		public string Reason { get; set; }
		/// <summary>Value set the section's headline code must belong to; null when it has no coded headline.</summary>
		public string PrimaryCodeSet { get; set; }
		public string SecondaryCodeSet { get; set; }
		/// <summary>Whether the report already carries a section of this kind.</summary>
		public bool Present { get; set; }
	}

	public class IncidentModuleData
	{
		public string ModuleId { get; set; }
		public int Kind { get; set; }
		public string KindName { get; set; }
		public string PayloadPath { get; set; }
		public string SchemaName { get; set; }
		public string PrimaryCode { get; set; }
		public string SecondaryCode { get; set; }
		public decimal? Quantity { get; set; }
		public string QuantityUnit { get; set; }
		public DateTime? OccurredOn { get; set; }
		/// <summary>The contract-shaped section body.</summary>
		public string DetailJson { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentResourceData
	{
		public string ResourceId { get; set; }
		public string ResourceCode { get; set; }
		public int? Quantity { get; set; }
		public string Detail { get; set; }
		public int Ordinal { get; set; }
	}

	/// <summary>
	/// A casualty or rescue. Demographics, the personnel link and the injury detail are restricted: a caller
	/// without RecordRestricted_View gets the entry with those fields absent and named in WithheldFields.
	/// </summary>
	public class IncidentCasualtyData
	{
		public string CasualtyId { get; set; }
		public int Kind { get; set; }
		public string KindName { get; set; }
		public string PersonType { get; set; }
		public string PersonnelUserId { get; set; }
		public string Rank { get; set; }
		public decimal? YearsOfService { get; set; }
		public string JobClassification { get; set; }
		public string BirthMonthYear { get; set; }
		public string Gender { get; set; }
		public string Race { get; set; }
		public bool? WasInjured { get; set; }
		public bool WasFatal { get; set; }
		public string CasualtyCause { get; set; }
		public string CasualtyAction { get; set; }
		public string CasualtyTimeline { get; set; }
		public string DutyType { get; set; }
		public List<string> Ppe { get; set; } = new List<string>();
		public string InjuryDetailJson { get; set; }
		public string RescueType { get; set; }
		public List<string> RescueActions { get; set; } = new List<string>();
		public List<string> RescueImpediments { get; set; } = new List<string>();
		public string RescueMode { get; set; }
		public string RescuePath { get; set; }
		public string RescueElevation { get; set; }
		public string PresenceKnown { get; set; }
		public DateTime? OccurredOn { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentExposureData
	{
		public string ExposureId { get; set; }
		public string LocationKind { get; set; }
		public string ItemType { get; set; }
		public string DamageType { get; set; }
		public string LocationUse { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public List<string> DisplacementCauses { get; set; } = new List<string>();
		public string AddressText { get; set; }
		public string Street { get; set; }
		public string Municipality { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public string CurrencyCode { get; set; }
		public int Ordinal { get; set; }
	}

	public class IncidentModuleInputData
	{
		public int Kind { get; set; }
		public string PrimaryCode { get; set; }
		public string SecondaryCode { get; set; }
		public decimal? Quantity { get; set; }
		public string QuantityUnit { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }
	}

	public class IncidentResourceInputData
	{
		public string ResourceCode { get; set; }
		public int? Quantity { get; set; }
		public string Detail { get; set; }
	}

	public class IncidentCasualtyInputData
	{
		public string CasualtyId { get; set; }
		public int Kind { get; set; }
		public string PersonType { get; set; }
		public string PersonnelUserId { get; set; }
		public string Rank { get; set; }
		public decimal? YearsOfService { get; set; }
		public string JobClassification { get; set; }
		public string BirthMonthYear { get; set; }
		public string Gender { get; set; }
		public string Race { get; set; }
		public bool? WasInjured { get; set; }
		public bool WasFatal { get; set; }
		public string CasualtyCause { get; set; }
		public string CasualtyAction { get; set; }
		public string CasualtyTimeline { get; set; }
		public string DutyType { get; set; }
		public List<string> Ppe { get; set; } = new List<string>();
		public string InjuryDetailJson { get; set; }
		public string RescueType { get; set; }
		public List<string> RescueActions { get; set; } = new List<string>();
		public List<string> RescueImpediments { get; set; } = new List<string>();
		public string RescueMode { get; set; }
		public string RescuePath { get; set; }
		public string RescueElevation { get; set; }
		public string PresenceKnown { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string DetailJson { get; set; }
	}

	public class IncidentExposureInputData
	{
		public string LocationKind { get; set; }
		public string ItemType { get; set; }
		public string DamageType { get; set; }
		public string LocationUse { get; set; }
		public bool? PeoplePresent { get; set; }
		public int? DisplacementCount { get; set; }
		public List<string> DisplacementCauses { get; set; } = new List<string>();
		public string AddressText { get; set; }
		public string Street { get; set; }
		public string Municipality { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public decimal? EstimatedValue { get; set; }
		public decimal? EstimatedLoss { get; set; }
		public string CurrencyCode { get; set; }
		public string DetailJson { get; set; }
	}

	/// <summary>409 on a stale incident report save.</summary>
	public class IncidentReportConflictResult : StandardApiResponseV4Base
	{
		public IncidentReportConflictData Data { get; set; }
	}

	public class IncidentReportConflictData
	{
		public string ReportId { get; set; }
		public long ExpectedRowVersion { get; set; }
		public long CurrentRowVersion { get; set; }
		public int CurrentState { get; set; }
		public string CurrentStateName { get; set; }
		public IncidentReportData Current { get; set; }
	}
}
