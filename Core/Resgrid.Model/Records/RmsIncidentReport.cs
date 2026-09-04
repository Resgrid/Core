using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// NERIS incident report aggregate root (RMS plan sections 4.2, 5.2, RMS-2; registry M0164). One per
	/// (DepartmentId, CallId, ReportingEntityId, DefinitionKey) — the SingleAuthoritative cardinality of
	/// section 5.2.1: one official incident report per responding agency, never one per company. Shares the
	/// lifecycle vocabulary of RmsOperationalRecord (RmsRecordState, RmsLifecycle) and the reference shape
	/// (DepartmentId, RecordId, RecordKind = IncidentReport, RevisionId) that RmsRevision, RmsAccessAudit,
	/// RmsSubmission and RmsSignature key on. Times live on the typed rows below; every prefilled value's
	/// provenance is an RmsSourceFact.
	/// </summary>
	[ProtoContract]
	public class RmsIncidentReport : IEntity
	{
		[ProtoMember(1)]
		public string RmsIncidentReportId { get; set; }

		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ProtoMember(3)]
		public string ProtectionId { get; set; }

		[ProtoMember(4)]
		public int CallId { get; set; }

		/// <summary>The department's NERIS entity ID at creation, or a department placeholder before a profile exists.</summary>
		[ProtoMember(5)]
		public string ReportingEntityId { get; set; }

		[ProtoMember(6)]
		public string DefinitionKey { get; set; }

		[ProtoMember(7)]
		public int DefinitionVersion { get; set; }

		/// <summary>Pinned NERIS contract version (value sets and payload shape) this report was authored against.</summary>
		[ProtoMember(8)]
		public string ProfileVersion { get; set; }

		[ProtoMember(9)]
		public int LifecyclePreset { get; set; }

		/// <summary><see cref="RmsRecordState"/>, including the reporting-destination states.</summary>
		[ProtoMember(10)]
		public int State { get; set; }

		[ProtoMember(11)]
		public string RecordNumber { get; set; }

		[ProtoMember(12)]
		public string DraftReference { get; set; }

		/// <summary>Department incident number sent to NERIS (Call.Number unless the author overrides it).</summary>
		[ProtoMember(13)]
		public string IncidentNumber { get; set; }

		[ProtoMember(14)]
		public string DisplaySummary { get; set; }

		[ProtoMember(15)]
		public int? StationGroupId { get; set; }

		[ProtoMember(16)]
		public string AuthorUserId { get; set; }

		[ProtoMember(17)]
		public string OwnerUserId { get; set; }

		[ProtoMember(18)]
		public string ReviewerUserId { get; set; }

		[ProtoMember(19)]
		public string ApproverUserId { get; set; }

		[ProtoMember(20)]
		public DateTime? ReviewDueOn { get; set; }

		[ProtoMember(21)]
		public DateTime? SubmittedForReviewOn { get; set; }

		[ProtoMember(22)]
		public DateTime? ReturnedOn { get; set; }

		[ProtoMember(23)]
		public string ReturnReasonCode { get; set; }

		[ProtoMember(24)]
		public string ReturnReasonText { get; set; }

		[ProtoMember(25)]
		public int ReturnCount { get; set; }

		[ProtoMember(26)]
		public DateTime? ApprovedOn { get; set; }

		[ProtoMember(27)]
		public DateTime? FinalizedOn { get; set; }

		[ProtoMember(28)]
		public string FinalizedByUserId { get; set; }

		[ProtoMember(29)]
		public string CurrentRevisionId { get; set; }

		[ProtoMember(30)]
		public int RevisionCount { get; set; }

		[ProtoMember(31)]
		public string AmendsRevisionId { get; set; }

		[ProtoMember(32)]
		public DateTime? VoidedOn { get; set; }

		[ProtoMember(33)]
		public string VoidedByUserId { get; set; }

		[ProtoMember(34)]
		public string VoidReasonCode { get; set; }

		[ProtoMember(35)]
		public string VoidReasonText { get; set; }

		[ProtoMember(36)]
		public DateTime? CancelledOn { get; set; }

		[ProtoMember(37)]
		public string CancelledByUserId { get; set; }

		/// <summary>NERIS-assigned incident ID once the first create succeeded; later submissions update it in place.</summary>
		[ProtoMember(38)]
		public string NerisIncidentId { get; set; }

		[ProtoMember(39)]
		public string LastSubmissionId { get; set; }

		/// <summary><see cref="RmsSubmissionState"/> of the latest submission, denormalized for queues.</summary>
		[ProtoMember(40)]
		public int? LastSubmissionState { get; set; }

		[ProtoMember(41)]
		public DateTime? LastSubmittedOn { get; set; }

		[ProtoMember(42)]
		public DateTime? AcceptedOn { get; set; }

		[ProtoMember(43)]
		public DateTime? RejectedOn { get; set; }

		/// <summary>Normalized, non-sensitive summary of the last rejection (codes and field paths, never payloads).</summary>
		[ProtoMember(44)]
		public string RejectionSummary { get; set; }

		// Dispatch-level facts (NERIS dispatch.*): prefilled from the Call with provenance rows, editable in Draft.
		[ProtoMember(53)]
		public DateTime? CallCreatedOn { get; set; }

		[ProtoMember(54)]
		public DateTime? CallAnsweredOn { get; set; }

		/// <summary>First unit on scene (NERIS call_arrival).</summary>
		[ProtoMember(55)]
		public DateTime? CallArrivalOn { get; set; }

		[ProtoMember(56)]
		public DateTime? IncidentClearedOn { get; set; }

		[ProtoMember(57)]
		public string DispatchCenterId { get; set; }

		[ProtoMember(58)]
		public string DeterminantCode { get; set; }

		/// <summary>The CAD/Resgrid incident code as dispatched (Call.Type), kept verbatim beside the mapped incident type.</summary>
		[ProtoMember(59)]
		public string DispatchIncidentCode { get; set; }

		[ProtoMember(60)]
		public string Disposition { get; set; }

		[ProtoMember(61)]
		public bool? PeoplePresent { get; set; }

		[ProtoMember(62)]
		public int? DisplacementCount { get; set; }

		[ProtoMember(63)]
		public int? AnimalsRescued { get; set; }

		/// <summary>Comma-separated NERIS special_modifiers codes.</summary>
		[ProtoMember(64)]
		public string SpecialModifiersCsv { get; set; }

		[ProtoMember(45)]
		public string IdempotencyKey { get; set; }

		[ProtoMember(46)]
		public int OriginClient { get; set; }

		[ProtoMember(47)]
		public DateTime CreatedOn { get; set; }

		[ProtoMember(48)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(49)]
		public DateTime ModifiedOn { get; set; }

		[ProtoMember(50)]
		public string ModifiedByUserId { get; set; }

		[ProtoMember(51)]
		public long RowVersion { get; set; }

		[ProtoMember(52)]
		public DateTime? DeletedOn { get; set; }

		public string RecordId => RmsIncidentReportId;

		public RmsRecordKind RecordKind => RmsRecordKind.IncidentReport;

		public object IdValue
		{
			get => RmsIncidentReportId;
			set => RmsIncidentReportId = (string)value;
		}

		public string TableName => "RmsIncidentReports";

		public string IdName => "RmsIncidentReportId";

		public int IdType => 1;

		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RecordId", "RecordKind" };
	}

	/// <summary>
	/// Provenance ledger for every prefilled value (plan section 4.2 "Timestamp authority"): where the value came
	/// from, what it was when imported, and what the author changed it to. Typed rows hold the current value;
	/// this row keeps the SourceKind and the original so provenance is never lost.
	/// </summary>
	public class RmsSourceFact : IEntity
	{
		public string RmsSourceFactId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		/// <summary>Null for the working draft; the revision copy on finalize.</summary>
		public string RevisionId { get; set; }
		/// <summary>Stable key such as dispatch.call_create, unit.{unitId}.on_scene, location.address.</summary>
		public string FactKey { get; set; }
		/// <summary><see cref="RmsSourceKind"/>.</summary>
		public int SourceKind { get; set; }
		public string SourceSystem { get; set; }
		public string SourceEntityType { get; set; }
		public string SourceEntityId { get; set; }
		/// <summary>The value as imported (ISO-8601 for times), retained verbatim.</summary>
		public string SourceValue { get; set; }
		/// <summary>The value currently on the typed row; differs from SourceValue once the author edited it.</summary>
		public string CurrentValue { get; set; }
		public DateTime? SourceTime { get; set; }
		public DateTime ImportedOn { get; set; }
		public DateTime? CorrectedOn { get; set; }
		public string CorrectedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsSourceFactId; set => RmsSourceFactId = (string)value; }
		public string TableName => "RmsSourceFacts";
		public string IdName => "RmsSourceFactId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Unit response on an incident report (NERIS unit_responses), with per-unit times and their provenance.</summary>
	public class RmsUnitResponse : IEntity
	{
		public string RmsUnitResponseId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public int? UnitId { get; set; }
		public string UnitNameSnapshot { get; set; }
		public string UnitTypeSnapshot { get; set; }
		public string UnitNerisId { get; set; }
		public int? StationGroupIdSnapshot { get; set; }
		public int? Staffing { get; set; }
		public bool UnableToDispatch { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? CanceledEnrouteOn { get; set; }
		public DateTime? StagingOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		/// <summary>NERIS response_mode: EMERGENT or NON_EMERGENT.</summary>
		public string ResponseMode { get; set; }
		public string TransportMode { get; set; }
		/// <summary><see cref="RmsSourceKind"/> of the prefilled times.</summary>
		public int TimesSourceKind { get; set; }
		public string Disposition { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsUnitResponseId; set => RmsUnitResponseId = (string)value; }
		public string TableName => "RmsUnitResponses";
		public string IdName => "RmsUnitResponseId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Versioned NERIS incident type assignment; LocalCode keeps the original Resgrid call type beside the mapped value.</summary>
	public class RmsIncidentType : IEntity
	{
		public string RmsIncidentTypeId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>NERIS incident_type value, e.g. FIRE||STRUCTURE_FIRE||...</summary>
		public string TypeCode { get; set; }
		public bool IsPrimary { get; set; }
		public string LocalCode { get; set; }
		public string ValueSetVersion { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsIncidentTypeId; set => RmsIncidentTypeId = (string)value; }
		public string TableName => "RmsIncidentTypes";
		public string IdName => "RmsIncidentTypeId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Action/tactic taken on the incident (NERIS actions_tactics), with actor, time, and provenance.</summary>
	public class RmsActionTactic : IEntity
	{
		public string RmsActionTacticId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string TacticCode { get; set; }
		public int? ActorUnitId { get; set; }
		public DateTime? OccurredOn { get; set; }
		public int SourceKind { get; set; }
		public string Outcome { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsActionTacticId; set => RmsActionTacticId = (string)value; }
		public string TableName => "RmsActionTactics";
		public string IdName => "RmsActionTacticId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Aid given or received on the single report (plan section 5.2.1: never a fabricated counterpart report).</summary>
	public class RmsAid : IEntity
	{
		public string RmsAidId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		/// <summary>GIVEN or RECEIVED.</summary>
		public string Direction { get; set; }
		/// <summary>ACTING_AS_AID, IN_LIEU_AID or SUPPORT_AID.</summary>
		public string AidType { get; set; }
		public string CounterpartNerisId { get; set; }
		public string CounterpartName { get; set; }
		public bool IsNonFireDepartment { get; set; }
		public string NonFdType { get; set; }
		public int Ordinal { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsAidId; set => RmsAidId = (string)value; }
		public string TableName => "RmsAids";
		public string IdName => "RmsAidId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Structured incident location snapshot (NERIS base.location) plus the geopoint.</summary>
	public class RmsLocation : IEntity
	{
		public string RmsLocationId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
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
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsLocationId; set => RmsLocationId = (string)value; }
		public string TableName => "RmsLocations";
		public string IdName => "RmsLocationId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Authored narrative sections; a protected-candidate holder, so it carries the inert envelope columns.</summary>
	public class RmsNarrative : IEntity
	{
		public string RmsNarrativeId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string Narrative { get; set; }
		public string ImpedimentNarrative { get; set; }
		public string OutcomeNarrative { get; set; }
		/// <summary>Supplemental department questions captured on the report; never enters the submission payload.</summary>
		public string SupplementalJson { get; set; }
		public bool IsProtected { get; set; }
		public string ProtectedEnvelope { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsNarrativeId; set => RmsNarrativeId = (string)value; }
		public string TableName => "RmsNarratives";
		public string IdName => "RmsNarrativeId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public enum RmsValidationSeverity
	{
		Error = 1,
		Warning = 2,
		Info = 3
	}

	public enum RmsValidationSource
	{
		Local = 1,
		Destination = 2
	}

	/// <summary>One validation finding against a profile/rule version; replaced on every validation run.</summary>
	public class RmsValidationIssue : IEntity
	{
		public string RmsValidationIssueId { get; set; }
		public int DepartmentId { get; set; }
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string ProfileVersion { get; set; }
		public string RuleKey { get; set; }
		/// <summary><see cref="RmsValidationSeverity"/>.</summary>
		public int Severity { get; set; }
		public string FieldPath { get; set; }
		public string Message { get; set; }
		/// <summary><see cref="RmsValidationSource"/>.</summary>
		public int Source { get; set; }
		public DateTime? ResolvedOn { get; set; }
		public DateTime CreatedOn { get; set; }

		public object IdValue { get => RmsValidationIssueId; set => RmsValidationIssueId = (string)value; }
		public string TableName => "RmsValidationIssues";
		public string IdName => "RmsValidationIssueId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
