using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Where a public-records request stands (RMS plan section 4.7, RMS-3). Persisted as an integer; append-only.
	/// </summary>
	public enum RmsDisclosureState
	{
		/// <summary>Logged with a requester and a statutory clock running.</summary>
		Received = 0,

		/// <summary>The scope query is being settled with the requester.</summary>
		Scoping = 1,

		/// <summary>The result set is being reviewed and redacted.</summary>
		InReview = 2,

		/// <summary>A redacted production exists but has not been released.</summary>
		Produced = 3,

		/// <summary>Released to the requester; the produced set is frozen.</summary>
		Released = 4,

		/// <summary>Refused under an exemption, with the reason recorded.</summary>
		Denied = 5,

		/// <summary>The requester withdrew it.</summary>
		Withdrawn = 6,

		/// <summary>Closed without release for any other recorded reason.</summary>
		Closed = 7
	}

	/// <summary>Named redaction profiles; a profile decides which classifications are withheld.</summary>
	public static class RmsRedactionProfiles
	{
		/// <summary>Withholds restricted content only; the ordinary public-records answer.</summary>
		public const string Standard = "Standard";

		/// <summary>Withholds restricted content and every participant identity.</summary>
		public const string NoPersonalIdentifiers = "NoPersonalIdentifiers";

		/// <summary>Releases everything the department holds; used for an internal or litigation production.</summary>
		public const string FullDisclosure = "FullDisclosure";
	}

	/// <summary>
	/// A public-records or access-to-information request (RMS plan section 4.7, registry M0171).
	/// <para>
	/// For a public agency this is a statutory obligation with a clock, which is why it is a record with a due
	/// date rather than an ad-hoc export. The jurisdiction profile matters because U.S. state public-records law
	/// and Canadian access-to-information regimes differ in what must be released and how quickly.
	/// </para>
	/// <para>
	/// The requester's identity is restricted: in most jurisdictions who asked is not itself public, and it must
	/// not leak into the produced packet.
	/// </para>
	/// </summary>
	public class RmsDisclosureRequest : IEntity
	{
		public string RmsDisclosureRequestId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>Department-facing reference, assigned on create.</summary>
		public string RequestNumber { get; set; }

		/// <summary>Restricted: who asked.</summary>
		public string RequesterName { get; set; }

		/// <summary>Restricted: the organisation they asked on behalf of.</summary>
		public string RequesterOrganization { get; set; }

		/// <summary>Restricted: contact details, kept only to answer the request.</summary>
		public string RequesterContact { get; set; }

		public DateTime ReceivedOn { get; set; }

		/// <summary>Computed from the department's statutory clock (setting 77) unless an administrator overrides it.</summary>
		public DateTime? StatutoryDueOn { get; set; }

		/// <summary>Which regime applies, e.g. <c>US-IL</c> or <c>CA-ON</c>; drives the clock and the exemptions.</summary>
		public string JurisdictionProfile { get; set; }

		/// <summary>What the requester asked for, in their words.</summary>
		public string ScopeNarrative { get; set; }

		/// <summary>The bounded <c>RmsRecordQuery</c> the scope resolves to; the same query the Records queue runs.</summary>
		public string ScopeQueryJson { get; set; }

		/// <summary><see cref="RmsDisclosureState"/>.</summary>
		public int State { get; set; }

		public string AssignedToUserId { get; set; }

		/// <summary>Redaction profile applied to productions unless one names its own.</summary>
		public string RedactionProfile { get; set; }

		public DateTime? ClosedOn { get; set; }

		public string ClosedByUserId { get; set; }

		/// <summary>Why it closed the way it did — the exemption relied on, or the requester's withdrawal.</summary>
		public string DispositionReason { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>Past its statutory due date and not yet resolved.</summary>
		public bool IsOverdue => StatutoryDueOn.HasValue && ClosedOn == null && StatutoryDueOn.Value < DateTime.UtcNow;

		public object IdValue { get => RmsDisclosureRequestId; set => RmsDisclosureRequestId = (string)value; }
		public string TableName => "RmsDisclosureRequests";
		public string IdName => "RmsDisclosureRequestId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOverdue" };
	}

	/// <summary>
	/// One immutable produced set for a request (RMS plan section 4.7, registry M0171).
	/// <para>
	/// A production is a new artifact, never a mutation of the source revisions — that is the whole point. It
	/// freezes exactly which record and revision IDs were released, under which redaction profile, with a
	/// checksum, so a later amendment to any of those records cannot silently change what the department is on
	/// the hook for having released.
	/// </para>
	/// </summary>
	public class RmsDisclosureProduction : IEntity
	{
		public string RmsDisclosureProductionId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string DisclosureRequestId { get; set; }

		/// <summary>1 for the first production, incrementing for supplemental ones.</summary>
		public int ProductionNumber { get; set; }

		/// <summary><see cref="RmsRedactionProfiles"/> actually applied.</summary>
		public string RedactionProfile { get; set; }

		/// <summary>
		/// The produced-set snapshot: the record and revision IDs released with their checksums at the moment of
		/// production. What makes "an amendment did not change what we released" provable.
		/// </summary>
		public string ProducedSetJson { get; set; }

		/// <summary>The redacted content itself, with its manifest.</summary>
		public string ArtifactJson { get; set; }

		/// <summary>Lower-case hex SHA-256 of <see cref="ArtifactJson"/>.</summary>
		public string Checksum { get; set; }

		public long ByteSize { get; set; }

		public int RecordCount { get; set; }

		/// <summary>Which fields were withheld and under what heading; the redaction log the requester may see.</summary>
		public string WithheldFieldsJson { get; set; }

		public int WithheldFieldCount { get; set; }

		public string PreparedByUserId { get; set; }

		public DateTime PreparedOn { get; set; }

		public string ReleasedByUserId { get; set; }
		public string DeliveryMethod { get; set; }
		public string DeliveryReference { get; set; }

		public DateTime? ReleasedOn { get; set; }

		public string ProtectedEnvelope { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public bool IsReleased => ReleasedOn.HasValue;

		public object IdValue { get => RmsDisclosureProductionId; set => RmsDisclosureProductionId = (string)value; }
		public string TableName => "RmsDisclosureProductions";
		public string IdName => "RmsDisclosureProductionId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsReleased" };
	}

	/// <summary>One record in a disclosure scope preview, before anything is produced.</summary>
	public class RmsDisclosureScopeItem
	{
		public RmsRecordKind RecordKind { get; set; } = RmsRecordKind.Operational;
		public string RecordId { get; set; }
		public string RecordNumber { get; set; }
		public string DefinitionKey { get; set; }
		public string Summary { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string CurrentRevisionId { get; set; }
		/// <summary>False when no saved revision is available for automatic production.</summary>
		public bool Producible { get; set; }
		public string NotProducibleReason { get; set; }
	}

	/// <summary>What a scope query resolves to, with the counts an officer needs before producing anything.</summary>
	public class RmsDisclosureScopePreview
	{
		public int MatchedCount { get; set; }
		public int ProducibleCount { get; set; }
		public int WithheldWholeRecordCount { get; set; }
		public bool Truncated { get; set; }
		public List<RmsDisclosureScopeItem> Items { get; set; } = new List<RmsDisclosureScopeItem>();
	}

	/// <summary>One withheld field in a production's redaction log.</summary>
	public class RmsRedactionEntry
	{
		public string Authority { get; set; }
		public string RecordId { get; set; }
		public string Section { get; set; }
		public string Field { get; set; }
		/// <summary>Why it was withheld — the classification or profile rule relied on.</summary>
		public string Basis { get; set; }
	}

	public class RmsDisclosureFieldValue
	{
		public string Path { get; set; }
		public string Value { get; set; }
	}
	public class RmsDisclosureFieldDecision
	{
		public string Path { get; set; }
		public bool Withhold { get; set; }
		public string Authority { get; set; }
		public string Basis { get; set; }
	}

	public class RmsDisclosureDownload
	{
		public byte[] Data { get; set; }
		public string ContentType { get; set; }
		public string FileName { get; set; }
	}
	public class RmsDisclosureReview
	{
		public string RequestId { get; set; }
		public string Profile { get; set; }
		public string ScopeChecksum { get; set; }
		public bool Reviewed { get; set; }
		public string Authority { get; set; }
		public string Basis { get; set; }
		/// <summary>Recorded handling of unfinished or inaccessible scope items; never silently omitted.</summary>
		public string UnresolvedScopeHandling { get; set; }
		public List<RmsDisclosureRecordReview> Records { get; set; } = new List<RmsDisclosureRecordReview>();
	}
	public class RmsDisclosureRecordReview
	{
		public RmsRecordKind RecordKind { get; set; }
		public string RecordId { get; set; }
		public string RecordNumber { get; set; }
		public string RevisionId { get; set; }
		public string RevisionChecksum { get; set; }
		public string ContentChecksum { get; set; }
		public bool WithholdWhole { get; set; }
		public string Authority { get; set; }
		public string Basis { get; set; }
		public List<string> AutomaticWithholds { get; set; } = new List<string>();
		public List<RmsDisclosureFieldValue> Fields { get; set; } = new List<RmsDisclosureFieldValue>();
		public List<RmsDisclosureFieldDecision> Decisions { get; set; } = new List<RmsDisclosureFieldDecision>();
		public List<RmsDisclosureAttachmentDecision> Attachments { get; set; } = new List<RmsDisclosureAttachmentDecision>();
	}
	public class RmsDisclosureAttachmentDecision
	{
		public RmsDisclosureAttachmentDerivative Derivative { get; set; }
		public List<RmsDisclosureFieldValue> Metadata { get; set; } = new List<RmsDisclosureFieldValue>();
		public string AttachmentId { get; set; }
		public string FileName { get; set; }
		public string Checksum { get; set; }
		public bool Include { get; set; }
		public bool Reviewed { get; set; }
		public string Authority { get; set; }
		public string Basis { get; set; }
	}
	/// <summary>A custodian-reviewed replacement file; its source remains unchanged.</summary>
	public class RmsDisclosureAttachmentDerivative
	{
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public byte[] Data { get; set; }
		public string Checksum { get; set; }
	}
}
