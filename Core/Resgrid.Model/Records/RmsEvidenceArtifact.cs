using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The six evidence sources RMS-3 ships, none optional (RMS plan sections 4.5 and RMS-3). Persisted as an
	/// integer; append-only.
	/// <para>
	/// Every one of these captures an authorized, bounded, server-authored snapshot with provenance. None of them
	/// hydrates or retains a live source: not a whole chat channel, not a unit's tracking history, not a source
	/// record that can later change or expire underneath the filing.
	/// </para>
	/// </summary>
	public enum RmsEvidenceKind
	{
		/// <summary>Apparatus/equipment readiness at the time of the call — a checklist/work-order manifest.</summary>
		ReadinessPacket = 1,

		/// <summary>The recorded dispatch decision: card and version, alarm level, mode, selected resources, shortfall.</summary>
		RunCardActivation = 2,

		/// <summary>Bounded unit tracking fixes over a coverage window, captured before source retention purges them.</summary>
		TrackingFix = 3,

		/// <summary>Selected incident-chat messages promoted into the record by an authorized member.</summary>
		ChatPromotion = 4,

		/// <summary>Supplies and controlled-substance usage from the inventory ledger.</summary>
		InventoryUsage = 5,

		/// <summary>Participant certification/qualification validity at the incident time.</summary>
		CertificationSnapshot = 6
	}

	/// <summary>
	/// How far an artifact's content may travel. Classification is decided at capture and never widened later,
	/// so an artifact captured as restricted stays restricted through print, export and disclosure.
	/// </summary>
	public enum RmsEvidenceClassification
	{
		/// <summary>Ordinary operational content; visible to anyone who can view the Record.</summary>
		Unrestricted = 0,

		/// <summary>Needs <c>RecordRestricted_View</c> wherever it is rendered.</summary>
		Restricted = 1,

		/// <summary>Protected-data candidate; the envelope columns carry it once a department enrolls (plan 5.9).</summary>
		Protected = 2
	}

	/// <summary>
	/// An immutable supporting artifact or manifest attached to a Record (RMS plan section 5.2, registry M0169).
	/// <para>
	/// The point of storing an artifact rather than a link is that a link can change or expire: a readiness report
	/// regenerated next year does not say what it said on the night of the fire. So the content is captured once,
	/// checksummed, classified and retained with the Record — and never updated. A correction is a new artifact,
	/// which is why there is no update path and why <see cref="SupersededByArtifactId"/> exists instead.
	/// </para>
	/// <para>
	/// The per-source rows the artifact was built from ride inside <see cref="ManifestJson"/>. That is deliberate:
	/// a manifest is the thing being attested to, and splitting it across a child table would let the two drift
	/// while the checksum still claimed the artifact was intact.
	/// </para>
	/// </summary>
	public class RmsEvidenceArtifact : IEntity
	{
		public string RmsEvidenceArtifactId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>The Record or incident report the artifact supports.</summary>
		public string RecordId { get; set; }

		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }

		/// <summary>
		/// The revision the artifact was captured into, or null while it supports the working draft. A finalize
		/// stamps the revision, which is what makes "the evidence as it stood at revision 3" answerable.
		/// </summary>
		public string RevisionId { get; set; }

		/// <summary><see cref="RmsEvidenceKind"/>.</summary>
		public int Kind { get; set; }

		/// <summary>Human title shown in the evidence list, server-authored.</summary>
		public string Title { get; set; }

		/// <summary>Why the capture happened; required, and written to the access audit.</summary>
		public string CaptureReason { get; set; }

		public string SourceSubsystem { get; set; }

		public string SourceEntityType { get; set; }

		/// <summary>Opaque, source-qualified identifier of the primary source row; never parsed for meaning.</summary>
		public string SourceEntityId { get; set; }

		public string IdentifierScheme { get; set; }

		/// <summary>Version or schema of the source at capture time, where the source carries one.</summary>
		public string SourceVersion { get; set; }
		/// <summary>Identity of the original capture request for safe API replay.</summary>
		public string CaptureRequestChecksum { get; set; }

		/// <summary>Start of the period the artifact covers (a tracking window, a readiness period).</summary>
		public DateTime? CoverageStart { get; set; }

		public DateTime? CoverageEnd { get; set; }

		/// <summary>The bounded, server-authored snapshot and its per-source manifest. The artifact itself.</summary>
		public string ManifestJson { get; set; }

		/// <summary>Lower-case hex SHA-256 of <see cref="ManifestJson"/>; what an auditor re-computes.</summary>
		public string Checksum { get; set; }

		public long ByteSize { get; set; }

		/// <summary>How many source rows the manifest covers; shown without opening the artifact.</summary>
		public int SourceItemCount { get; set; }

		/// <summary>Reserved for an external blob location; null while the manifest is stored in-row.</summary>
		public string StorageReference { get; set; }

		/// <summary><see cref="RmsEvidenceClassification"/>, decided at capture and never widened.</summary>
		public int Classification { get; set; }

		/// <summary>
		/// Retention in years for this artifact when it must outlive the Record's own policy; null inherits the
		/// Record, and 0 is permanent. Legal holds are evaluated against the Record and cover its artifacts.
		/// </summary>
		public int? RetentionYears { get; set; }

		public string CapturedByUserId { get; set; }

		public DateTime CapturedOn { get; set; }

		/// <summary><see cref="RmsOriginClient"/> the capture came from.</summary>
		public int OriginClient { get; set; }

		/// <summary>Set when a later capture replaces this one; the original still stands and is still readable.</summary>
		public string SupersededByArtifactId { get; set; }

		public DateTime? SupersededOn { get; set; }

		/// <summary>Inert Protected Data envelope (plan section 5.9.1); null until the department enrolls.</summary>
		public string ProtectedEnvelope { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>A superseded artifact is history: readable, never the current evidence of its kind.</summary>
		public bool IsCurrent => !SupersededOn.HasValue && !DeletedOn.HasValue;

		public object IdValue { get => RmsEvidenceArtifactId; set => RmsEvidenceArtifactId = (string)value; }
		public string TableName => "RmsEvidenceArtifacts";
		public string IdName => "RmsEvidenceArtifactId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsCurrent" };
	}

	/// <summary>What a caller asks an evidence adapter to capture. The adapter decides what it can honour.</summary>
	public class RecordEvidenceCaptureRequest
	{
		/// <summary>Optional caller version; the service pins the observed parent version when omitted.</summary>
		public long? ExpectedRowVersion { get; set; }
		public int DepartmentId { get; set; }

		public string RecordId { get; set; }

		public RmsRecordKind RecordKind { get; set; } = RmsRecordKind.Operational;

		public RmsEvidenceKind Kind { get; set; }

		/// <summary>Required: why this evidence is being put into an official record.</summary>
		public string CaptureReason { get; set; }

		/// <summary>The Call the Record hangs off, where the source is call-scoped.</summary>
		public int? CallId { get; set; }

		/// <summary>Bounded window for time-series sources; the adapter clamps it to its own maximum.</summary>
		public DateTime? CoverageStart { get; set; }

		public DateTime? CoverageEnd { get; set; }

		/// <summary>Explicit source ids the caller selected — chat messages, units, members.</summary>
		public List<string> SourceIds { get; set; } = new List<string>();

		public List<int> UnitIds { get; set; } = new List<int>();

		public List<string> UserIds { get; set; } = new List<string>();

		public string CapturedByUserId { get; set; }

		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
	}

	/// <summary>What an adapter produced, before the service persists it.</summary>
	public class RecordEvidenceCapture
	{
		public bool Available { get; set; } = true;

		/// <summary>Why nothing was captured; shown to the author rather than failing silently.</summary>
		public string UnavailableReason { get; set; }

		public string Title { get; set; }

		public string SourceSubsystem { get; set; }

		public string SourceEntityType { get; set; }

		public string SourceEntityId { get; set; }

		public string IdentifierScheme { get; set; }

		public string SourceVersion { get; set; }

		public DateTime? CoverageStart { get; set; }

		public DateTime? CoverageEnd { get; set; }

		/// <summary>The manifest object; the service serializes it deterministically and checksums the result.</summary>
		public object Manifest { get; set; }

		public int SourceItemCount { get; set; }

		public RmsEvidenceClassification Classification { get; set; } = RmsEvidenceClassification.Unrestricted;

		public int? RetentionYears { get; set; }

		public static RecordEvidenceCapture Unavailable(string reason)
		{
			return new RecordEvidenceCapture { Available = false, UnavailableReason = reason };
		}
	}
}
