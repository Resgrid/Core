using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Records
{
	#region Evidence

	public class RecordEvidenceSourcesResult : StandardApiResponseV4Base
	{
		public List<RecordEvidenceSourceData> Data { get; set; } = new List<RecordEvidenceSourceData>();
	}

	/// <summary>
	/// Whether one of the six evidence sources can produce anything for this department right now. "Unavailable
	/// with a reason" is a real answer and is not the same as "there was no evidence", so the client renders the
	/// source either way rather than hiding it.
	/// </summary>
	public class RecordEvidenceSourceData
	{
		public int Kind { get; set; }
		public string KindName { get; set; }
		public bool Available { get; set; }
		public string Reason { get; set; }
	}

	public class RecordEvidenceListResult : StandardApiResponseV4Base
	{
		public List<RecordEvidenceArtifactData> Data { get; set; } = new List<RecordEvidenceArtifactData>();
	}

	public class RecordEvidenceResult : StandardApiResponseV4Base
	{
		public RecordEvidenceArtifactData Data { get; set; }
	}

	/// <summary>
	/// An immutable evidence artifact. <see cref="ManifestJson"/> is only populated on the single-artifact read,
	/// and only for a caller allowed to see that artifact's classification — a list of twenty artifacts must not
	/// ship twenty manifests, and a restricted manifest must not ship at all to an unrestricted caller.
	/// </summary>
	public class RecordEvidenceArtifactData
	{
		public string ArtifactId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public int Kind { get; set; }
		public string KindName { get; set; }
		public string Title { get; set; }
		public string CaptureReason { get; set; }
		public string SourceSubsystem { get; set; }
		public string SourceEntityType { get; set; }
		public string SourceEntityId { get; set; }
		public string IdentifierScheme { get; set; }
		public string SourceVersion { get; set; }
		public DateTime? CoverageStart { get; set; }
		public DateTime? CoverageEnd { get; set; }
		public string Checksum { get; set; }
		public long ByteSize { get; set; }
		public int SourceItemCount { get; set; }
		public int Classification { get; set; }
		public string ClassificationName { get; set; }
		public int? RetentionYears { get; set; }
		public string CapturedByUserId { get; set; }
		public DateTime CapturedOn { get; set; }
		public int OriginClient { get; set; }
		public string SupersededByArtifactId { get; set; }
		public DateTime? SupersededOn { get; set; }
		public bool IsCurrent { get; set; }
		/// <summary>The artifact body; null in list responses and null when the caller may not see its classification.</summary>
		public string ManifestJson { get; set; }
		/// <summary>True when the manifest was withheld rather than simply not requested.</summary>
		public bool ManifestWithheld { get; set; }
	}

	/// <summary>Capture one artifact. The server bounds the window and decides the classification; neither is a client input.</summary>
	public class CaptureRecordEvidenceInput
	{
		public string RecordId { get; set; }
		/// <summary>RmsRecordKind; Operational by default, Incident for a NERIS report.</summary>
		public int? RecordKind { get; set; }
		public int Kind { get; set; }
		/// <summary>Required: why this evidence is being put into an official record.</summary>
		public string CaptureReason { get; set; }
		public int? CallId { get; set; }
		public DateTime? CoverageStart { get; set; }
		public DateTime? CoverageEnd { get; set; }
		public List<string> SourceIds { get; set; } = new List<string>();
		public List<int> UnitIds { get; set; } = new List<int>();
		public List<string> UserIds { get; set; } = new List<string>();
		public string IdempotencyKey { get; set; }
		public int? OriginClient { get; set; }
	}

	public class RecordEvidenceVerifyResult : StandardApiResponseV4Base
	{
		public RecordEvidenceVerifyData Data { get; set; }
	}

	public class RecordEvidenceVerifyData
	{
		public string ArtifactId { get; set; }
		public bool Intact { get; set; }
		public string Checksum { get; set; }
	}

	#endregion

	#region Disclosures

	public class DisclosureRequestsResult : StandardApiResponseV4Base
	{
		public List<DisclosureRequestData> Data { get; set; } = new List<DisclosureRequestData>();
		public int Total { get; set; }
	}

	public class DisclosureRequestResult : StandardApiResponseV4Base
	{
		public DisclosureRequestData Data { get; set; }
	}

	/// <summary>
	/// A public-records request. Who asked is restricted in most jurisdictions, so the requester fields are
	/// withheld — and named in <see cref="WithheldFields"/> — from a caller without RecordRestricted_View.
	/// </summary>
	public class DisclosureRequestData
	{
		public string RequestId { get; set; }
		public string RequestNumber { get; set; }
		public string RequesterName { get; set; }
		public string RequesterOrganization { get; set; }
		public string RequesterContact { get; set; }
		public DateTime ReceivedOn { get; set; }
		public DateTime? StatutoryDueOn { get; set; }
		public bool IsOverdue { get; set; }
		public string JurisdictionProfile { get; set; }
		public string ScopeNarrative { get; set; }
		public string ScopeQueryJson { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public string AssignedToUserId { get; set; }
		public string RedactionProfile { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string ClosedByUserId { get; set; }
		public string DispositionReason { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public string ETag { get; set; }
		public List<string> WithheldFields { get; set; } = new List<string>();
	}

	public class CreateDisclosureRequestInput
	{
		public string RequesterName { get; set; }
		public string RequesterOrganization { get; set; }
		public string RequesterContact { get; set; }
		public DateTime? ReceivedOn { get; set; }
		public DateTime? StatutoryDueOn { get; set; }
		public string JurisdictionProfile { get; set; }
		public string ScopeNarrative { get; set; }
		public string AssignedToUserId { get; set; }
		public string RedactionProfile { get; set; }
		public string IdempotencyKey { get; set; }
	}

	/// <summary>The scope a production runs against, expressed as the same bounded query the Records queue runs.</summary>
	public class SaveDisclosureScopeInput
	{
		public string RequestId { get; set; }
		public string ScopeNarrative { get; set; }
		public string RedactionProfile { get; set; }
		public DisclosureScopeQueryInput Scope { get; set; }
	}

	public class DisclosureScopeQueryInput
	{
		public List<int> States { get; set; }
		public string DefinitionKey { get; set; }
		public int? Year { get; set; }
		public int? CallId { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public int? StationGroupId { get; set; }
		public bool IncludeLegacy { get; set; }
		public int? Take { get; set; }
	}

	public class DisclosureScopePreviewResult : StandardApiResponseV4Base
	{
		public DisclosureScopePreviewData Data { get; set; }
	}

	public class DisclosureScopePreviewData
	{
		public int MatchedCount { get; set; }
		public int ProducibleCount { get; set; }
		public int WithheldWholeRecordCount { get; set; }
		public bool Truncated { get; set; }
		public List<DisclosureScopeItemData> Items { get; set; } = new List<DisclosureScopeItemData>();
	}

	public class DisclosureScopeItemData
	{
		public string RecordId { get; set; }
		public string RecordNumber { get; set; }
		public string DefinitionKey { get; set; }
		public string Summary { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string CurrentRevisionId { get; set; }
		public bool Producible { get; set; }
		public string NotProducibleReason { get; set; }
	}

	public class DisclosureProductionsResult : StandardApiResponseV4Base
	{
		public List<DisclosureProductionData> Data { get; set; } = new List<DisclosureProductionData>();
	}

	public class DisclosureProductionResult : StandardApiResponseV4Base
	{
		public DisclosureProductionData Data { get; set; }
	}

	/// <summary>
	/// One immutable produced set. The redacted artifact itself is only returned on the single-production read,
	/// so listing a request's productions never ships several megabytes of released content.
	/// </summary>
	public class DisclosureProductionData
	{
		public string ProductionId { get; set; }
		public string RequestId { get; set; }
		public int ProductionNumber { get; set; }
		public string RedactionProfile { get; set; }
		public string Checksum { get; set; }
		public long ByteSize { get; set; }
		public int RecordCount { get; set; }
		public int WithheldFieldCount { get; set; }
		public string PreparedByUserId { get; set; }
		public DateTime PreparedOn { get; set; }
		public string ReleasedByUserId { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public bool IsReleased { get; set; }
		/// <summary>Record and revision ids released, with their checksums at production time.</summary>
		public string ProducedSetJson { get; set; }
		/// <summary>The redaction log: which fields were withheld and under what heading.</summary>
		public string WithheldFieldsJson { get; set; }
		/// <summary>The redacted content; single-production read only.</summary>
		public string ArtifactJson { get; set; }
	}

	public class DisclosureCommandInput
	{
		public string RequestId { get; set; }
		public string ProductionId { get; set; }
		public string RedactionProfile { get; set; }
		/// <summary>RmsDisclosureState for a close: Denied, Withdrawn or Closed.</summary>
		public int? Disposition { get; set; }
		public string Reason { get; set; }
		public string IdempotencyKey { get; set; }
	}

	public class DisclosureVerifyResult : StandardApiResponseV4Base
	{
		public DisclosureVerifyData Data { get; set; }
	}

	public class DisclosureVerifyData
	{
		public string ProductionId { get; set; }
		public bool Intact { get; set; }
	}

	#endregion

	#region Dashboard

	public class RecordsDashboardResult : StandardApiResponseV4Base
	{
		public RecordsDashboardData Data { get; set; }
	}

	/// <summary>The Records work queues, group-scope-aware. A count that could not be produced degrades into <see cref="Warnings"/>.</summary>
	public class RecordsDashboardData
	{
		public DateTime GeneratedOn { get; set; }
		public int OperationalDrafts { get; set; }
		public int OperationalAwaitingReview { get; set; }
		public int OperationalReturned { get; set; }
		public int IncidentIncomplete { get; set; }
		public int IncidentAwaitingReview { get; set; }
		public int IncidentSubmitted { get; set; }
		public int IncidentAccepted { get; set; }
		public int IncidentRejected { get; set; }
		public int Overdue { get; set; }
		public int AnalysesAwaitingFiling { get; set; }
		public int DisclosuresOpen { get; set; }
		public int DisclosuresOverdue { get; set; }
		public List<string> Warnings { get; set; } = new List<string>();
	}

	public class NerisCrosswalkCoverageResult : StandardApiResponseV4Base
	{
		public NerisCrosswalkCoverageData Data { get; set; }
	}

	/// <summary>Crosswalk gap report: an unmapped or stale local code is a filing that will need manual work.</summary>
	public class NerisCrosswalkCoverageData
	{
		public string ContractVersion { get; set; }
		public int TotalLocalCodes { get; set; }
		public int MappedCount { get; set; }
		public int UnmappedCount { get; set; }
		public int StaleMappingCount { get; set; }
		public List<NerisCrosswalkCoverageItemData> Items { get; set; } = new List<NerisCrosswalkCoverageItemData>();
		public List<string> Warnings { get; set; } = new List<string>();
	}

	public class NerisCrosswalkCoverageItemData
	{
		public string SetKey { get; set; }
		public string LocalCode { get; set; }
		public string NerisCode { get; set; }
		public bool Mapped { get; set; }
	}

	#endregion
}
