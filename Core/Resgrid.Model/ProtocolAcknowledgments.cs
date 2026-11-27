using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where one member's acknowledgment of one version stands (ProtocolAcknowledgments.Status, M0348). Persisted: append only.</summary>
	public enum ProtocolAcknowledgmentStatuses
	{
		/// <summary>Assigned and not yet signed.</summary>
		Open = 0,

		/// <summary>Signed (and, for a quiz-gated version, the training passed): complete. Never withdrawn or deleted by a user action.</summary>
		Acknowledged = 1,

		/// <summary>Signed, waiting for the linked baseline Training to be passed (T0 Passed, not Complete).</summary>
		AwaitingQuiz = 2,

		/// <summary>The member left the audience (or the department) before signing; kept for history, reopened if they come back.</summary>
		Withdrawn = 3,

		/// <summary>A newer version came into force before this one was signed (the member owes the newer one instead).</summary>
		Superseded = 4
	}

	/// <summary>How an acknowledgment came to be assigned. Persisted: append only.</summary>
	public enum ProtocolAcknowledgmentSources
	{
		/// <summary>The version was published (or scheduled) to the audience.</summary>
		Publish = 0,

		/// <summary>The member entered the audience of a version in force (R-P3: onboarding bundle, a new role or group).</summary>
		AudienceEntry = 1,

		/// <summary>A priority bulletin (R-P7).</summary>
		Bulletin = 2,

		/// <summary>A minor version carried the member's signed acknowledgment of the version before it forward.</summary>
		CarriedForward = 3
	}

	/// <summary>The reminders worker 86 has sent for an open acknowledgment. Persisted.</summary>
	public enum ProtocolAcknowledgmentReminderStages
	{
		None = 0,

		/// <summary>The due date has come.</summary>
		Due = 1,

		/// <summary>Overdue (<see cref="ProtocolAcknowledgmentRules.OverdueReminderDays"/> after the due date).</summary>
		Overdue = 2,

		/// <summary>Escalated to the member's supervisor / group admin (<see cref="ProtocolAcknowledgmentRules.EscalationDays"/> after the due date).</summary>
		Escalated = 3
	}

	/// <summary>The answer to a read-and-sign (web, or v4 from Omni with the version id and hash the member read, R-P11).</summary>
	public enum ProtocolAcknowledgeOutcomes
	{
		Acknowledged = 0,

		/// <summary>Signed; completes when the linked training is passed.</summary>
		AwaitingQuiz = 1,

		/// <summary>The member owes no acknowledgment of this version (not in its audience, or the version needs none).</summary>
		NotAssigned = 2,

		/// <summary>The member has not opened the version yet (the web requires reading before signing).</summary>
		NotOpened = 3,

		/// <summary>The version is no longer the one in force: read the newer one (<see cref="ProtocolAcknowledgeResult.CurrentVersionId"/>).</summary>
		ReadNewerVersion = 4,

		/// <summary>The content the member read is not the version's content (the hash does not match).</summary>
		ContentChanged = 5,

		/// <summary>The department requires a step-up (L2) for read-and-sign and the proof does not meet it.</summary>
		StepUpRequired = 6,

		/// <summary>Already signed: nothing changed.</summary>
		AlreadyAcknowledged = 7,

		InvalidRequest = 8
	}

	/// <summary>
	/// One member's acknowledgment of one protocol version (P2, M0348): assigned on publish and whenever the member enters the
	/// audience (R-P3), signed at L1 (or L2 when the department asks) bound to the version's content hash, carried forward by a minor
	/// version, withdrawn when the member leaves the audience before signing. Signed rows are kept (R-P10) and written to the
	/// transcript (LearningRecords, source ProtocolAcknowledgment).
	/// </summary>
	[Table("ProtocolAcknowledgments")]
	public class ProtocolAcknowledgment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolAcknowledgmentId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="ProtocolAcknowledgmentStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="ProtocolAcknowledgmentSources"/>.</summary>
		public int Source { get; set; }

		public DateTime AssignedOn { get; set; }

		public DateTime? DueOn { get; set; }

		/// <summary>The day the member entered the audience (audience-entry assignments; the due date counts from it).</summary>
		public DateTime? EnteredAudienceOn { get; set; }

		/// <summary>The protocol's place in the onboarding bundle when it was assigned.</summary>
		public int BundleOrder { get; set; }

		/// <summary>The first time the member opened this version (read-and-sign requires it on the web).</summary>
		public DateTime? OpenedOn { get; set; }

		public DateTime? SignedOn { get; set; }

		/// <summary>The read-and-sign signature (SignatureRecords, subject ProtocolAcknowledgment = the version id).</summary>
		public int? SignatureRecordId { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>The baseline Training whose pass completes the acknowledgment (from the version).</summary>
		public int? QuizTrainingId { get; set; }

		/// <summary>The signed acknowledgment of the earlier version a minor version carried forward.</summary>
		public int? CarriedFromAcknowledgmentId { get; set; }

		public DateTime? WithdrawnOn { get; set; }

		/// <summary><see cref="ProtocolAcknowledgmentReminderStages"/> sent.</summary>
		public int ReminderStage { get; set; }

		public DateTime? LastReminderOn { get; set; }

		public DateTime? EscalatedOn { get; set; }

		/// <summary>Signed from an offline queue (Omni, R-P11).</summary>
		public bool ViaOffline { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsOpen => Status == (int)ProtocolAcknowledgmentStatuses.Open;

		[NotMapped]
		[JsonIgnore]
		public bool IsSigned => SignedOn.HasValue && (Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged || Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolAcknowledgmentId; }
			set { ProtocolAcknowledgmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolAcknowledgments";

		[NotMapped]
		public string IdName => "ProtocolAcknowledgmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen", "IsSigned" };
	}

	/// <summary>What assigning one member a version should do (pure planning, <see cref="ProtocolAcknowledgmentRules.Plan"/>).</summary>
	public enum ProtocolAcknowledgmentPlanActions
	{
		/// <summary>The member already owes, or has given, this version's acknowledgment.</summary>
		None = 0,

		/// <summary>A new open assignment.</summary>
		Assign = 1,

		/// <summary>A minor version: the member's signed acknowledgment of the version before it carries forward, signed.</summary>
		CarryForward = 2,

		/// <summary>A withdrawn assignment comes back (the member re-entered the audience).</summary>
		Reopen = 3
	}

	/// <summary>The rules of P2 distribution and acknowledgment, kept pure for tests.</summary>
	public static class ProtocolAcknowledgmentRules
	{
		/// <summary>Days to acknowledge when the protocol sets none (R-P3: per protocol, default 30).</summary>
		public const int DefaultDueDays = 30;

		public const int MaxDueDays = 365;

		/// <summary>Days after the due date for the overdue reminder.</summary>
		public const int OverdueReminderDays = 3;

		/// <summary>Days after the due date for the escalation to the member's supervisor or group admin.</summary>
		public const int EscalationDays = 7;

		/// <summary>The longest a priority bulletin can stay in force.</summary>
		public const int MaxBulletinDays = 90;

		public const int BulletinTitleMaxLength = 100;

		/// <summary>Bulletins are short: their text is capped (a longer one belongs in a protocol).</summary>
		public const int BulletinTextMaxLength = 4000;

		public static int DueDays(int? protocolDays) =>
			protocolDays.HasValue && protocolDays.Value > 0 ? Math.Min(protocolDays.Value, MaxDueDays) : DefaultDueDays;

		/// <summary>
		/// When an assignment is due: <paramref name="dueDays"/> after it was assigned (the publish, or the day the member entered the
		/// audience). A version scheduled for a later effective date is due on that date (P2: acknowledgeable from publish, the due
		/// date defaults to the effective date). A bulletin is due by its expiry when that comes first.
		/// </summary>
		public static DateTime DueOn(DateTime assignedOnUtc, int dueDays, DateTime? effectiveOnUtc, DateTime? expiresOnUtc = null)
		{
			var due = effectiveOnUtc.HasValue && effectiveOnUtc.Value > assignedOnUtc
				? effectiveOnUtc.Value
				: assignedOnUtc.AddDays(dueDays > 0 ? dueDays : DefaultDueDays);
			if (expiresOnUtc.HasValue && expiresOnUtc.Value > assignedOnUtc && expiresOnUtc.Value < due)
				due = expiresOnUtc.Value;
			return due;
		}

		/// <summary>True when <paramref name="version"/> is a minor revision of <paramref name="previous"/> (same major, later minor).</summary>
		public static bool IsMinorOf(ProtocolVersion version, ProtocolVersion previous) =>
			version != null && previous != null && version.Major > 0 && previous.Major > 0 && version.Major == previous.Major &&
			version.Minor > previous.Minor && version.DispatchProtocolId == previous.DispatchProtocolId;

		/// <summary>
		/// What assigning <paramref name="version"/> to one member does, given their row for this version (if any) and their row for
		/// the version before it (if any). A minor version carries a signed acknowledgment forward; a major one assigns again.
		/// </summary>
		public static ProtocolAcknowledgmentPlanActions Plan(ProtocolVersion version, ProtocolAcknowledgment existing, ProtocolVersion previous,
			ProtocolAcknowledgment previousAcknowledgment)
		{
			if (version == null || !version.RequiresAcknowledgment)
				return ProtocolAcknowledgmentPlanActions.None;

			if (existing != null)
				return existing.Status == (int)ProtocolAcknowledgmentStatuses.Withdrawn ? ProtocolAcknowledgmentPlanActions.Reopen : ProtocolAcknowledgmentPlanActions.None;

			if (previousAcknowledgment != null && previousAcknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged && IsMinorOf(version, previous) &&
				previousAcknowledgment.ProtocolVersionId == previous.ProtocolVersionId)
				return ProtocolAcknowledgmentPlanActions.CarryForward;

			return ProtocolAcknowledgmentPlanActions.Assign;
		}

		/// <summary>
		/// Whether a version can be acknowledged now: the one in force (Published and the protocol's current version), or an approved
		/// version scheduled for a later effective date (acknowledgeable from publish). Anything superseded, retired, in draft or in
		/// review is not: the member is asked to read the version in force instead (R-P11).
		/// </summary>
		public static bool IsAcknowledgeable(DispatchProtocol protocol, ProtocolVersion version)
		{
			if (protocol == null || version == null || protocol.IsDeleted || version.DispatchProtocolId != protocol.DispatchProtocolId)
				return false;

			if (version.Status == (int)ProtocolVersionStatuses.Published)
				return protocol.CurrentVersionId == version.ProtocolVersionId && protocol.Status == (int)ProtocolStatuses.Published;

			return version.Status == (int)ProtocolVersionStatuses.Approved && version.EffectiveOn.HasValue && version.Major > 0;
		}

		/// <summary>
		/// The offline rule (R-P11): an acknowledgment queued with the version id and the hash the member read is accepted only when
		/// that version can still be acknowledged and its hash is the one read; otherwise the member reads the newer version.
		/// </summary>
		public static ProtocolAcknowledgeOutcomes CheckOffline(DispatchProtocol protocol, ProtocolVersion version, string contentHashRead)
		{
			if (protocol == null || version == null || string.IsNullOrWhiteSpace(contentHashRead))
				return ProtocolAcknowledgeOutcomes.InvalidRequest;

			if (!IsAcknowledgeable(protocol, version))
				return ProtocolAcknowledgeOutcomes.ReadNewerVersion;

			if (!string.Equals(version.ContentHash, contentHashRead.Trim(), StringComparison.OrdinalIgnoreCase))
				return ProtocolAcknowledgeOutcomes.ContentChanged;

			return ProtocolAcknowledgeOutcomes.Acknowledged;
		}

		/// <summary>The reminder stage owed on <paramref name="utcNow"/> for an open acknowledgment due on <paramref name="dueOnUtc"/>.</summary>
		public static ProtocolAcknowledgmentReminderStages DueStage(DateTime? dueOnUtc, DateTime utcNow)
		{
			if (!dueOnUtc.HasValue || utcNow < dueOnUtc.Value)
				return ProtocolAcknowledgmentReminderStages.None;
			if (utcNow >= dueOnUtc.Value.AddDays(EscalationDays))
				return ProtocolAcknowledgmentReminderStages.Escalated;
			if (utcNow >= dueOnUtc.Value.AddDays(OverdueReminderDays))
				return ProtocolAcknowledgmentReminderStages.Overdue;
			return ProtocolAcknowledgmentReminderStages.Due;
		}

		/// <summary>The status a signature leaves: complete, unless the version is quiz-gated and the training is not passed yet.</summary>
		public static ProtocolAcknowledgmentStatuses StatusAfterSigning(int? quizTrainingId, bool quizPassed) =>
			quizTrainingId.HasValue && !quizPassed ? ProtocolAcknowledgmentStatuses.AwaitingQuiz : ProtocolAcknowledgmentStatuses.Acknowledged;

		/// <summary>
		/// With the LMS question-bank quiz as well (round 5F): the signature completes the acknowledgment only when every quiz it asks for is
		/// passed, the linked training (free) and the bank quiz (LMS); otherwise it waits for them.
		/// </summary>
		public static ProtocolAcknowledgmentStatuses StatusAfterSigning(int? quizTrainingId, bool trainingPassed, bool bankQuizRequired, bool bankQuizPassed) =>
			(quizTrainingId.HasValue && !trainingPassed) || (bankQuizRequired && !bankQuizPassed)
				? ProtocolAcknowledgmentStatuses.AwaitingQuiz
				: ProtocolAcknowledgmentStatuses.Acknowledged;

		/// <summary>Whether an acknowledgment counts as done in the compliance grid and the transcript.</summary>
		public static bool IsComplete(ProtocolAcknowledgment acknowledgment) =>
			acknowledgment != null && acknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged;

		public static bool IsOverdue(ProtocolAcknowledgment acknowledgment, DateTime utcNow) =>
			acknowledgment != null && (acknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.Open || acknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz) &&
			acknowledgment.DueOn.HasValue && acknowledgment.DueOn.Value < utcNow;

		/// <summary>Whether a row is still owed (open, or signed and waiting for the quiz).</summary>
		public static bool IsOutstanding(ProtocolAcknowledgment acknowledgment) =>
			acknowledgment != null && (acknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.Open || acknowledgment.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz);

		/// <summary>Standards tags normalized: trimmed, de-duplicated, at most 25, capped in length.</summary>
		public static string NormalizeTags(string tags)
		{
			if (string.IsNullOrWhiteSpace(tags))
				return null;
			var list = tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToList();
			var joined = string.Join(", ", list);
			return joined.Length == 0 ? null : joined.Length > DispatchProtocol.TagsMaxLength ? joined.Substring(0, DispatchProtocol.TagsMaxLength) : joined;
		}

		/// <summary>The standards most departments map to (offered as suggestions; any custom tag is allowed).</summary>
		public static IReadOnlyList<string> SuggestedStandards { get; } = new[] { "CFAI", "CAAS", "CALEA", "NFPA", "ISO 45001" };
	}

	/// <summary>The result of a read-and-sign.</summary>
	public sealed class ProtocolAcknowledgeResult
	{
		public ProtocolAcknowledgeOutcomes Outcome { get; set; }

		public ProtocolAcknowledgment Acknowledgment { get; set; }

		/// <summary>The version in force, when the member must read a newer one.</summary>
		public int? CurrentVersionId { get; set; }

		public string CurrentContentHash { get; set; }

		public bool Succeeded => Outcome == ProtocolAcknowledgeOutcomes.Acknowledged || Outcome == ProtocolAcknowledgeOutcomes.AwaitingQuiz ||
			Outcome == ProtocolAcknowledgeOutcomes.AlreadyAcknowledged;

		public static ProtocolAcknowledgeResult Of(ProtocolAcknowledgeOutcomes outcome) => new ProtocolAcknowledgeResult { Outcome = outcome };
	}

	/// <summary>One line of a member's "My acknowledgments" (web and v4).</summary>
	public sealed class ProtocolAcknowledgmentItem
	{
		public ProtocolAcknowledgment Acknowledgment { get; set; }

		public int ProtocolId { get; set; }

		public string Code { get; set; }

		public string Name { get; set; }

		public int Kind { get; set; }

		public bool IsBulletin { get; set; }

		public string VersionLabel { get; set; }

		public string ContentHash { get; set; }

		public DateTime? EffectiveOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public bool IsOverdue { get; set; }

		/// <summary>The linked training (quiz-gated acknowledgment), with whether the member has passed it.</summary>
		public int? QuizTrainingId { get; set; }

		public string QuizTrainingName { get; set; }

		public bool QuizPassed { get; set; }

		/// <summary>Signed and still waiting for a quiz other than the linked training (the LMS question-bank quiz).</summary>
		public bool BankQuizOwed { get; set; }
	}

	/// <summary>One person's line in the per-version compliance grid.</summary>
	public sealed class ProtocolComplianceRow
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public string GroupName { get; set; }

		public int? GroupId { get; set; }

		public ProtocolAcknowledgment Acknowledgment { get; set; }

		/// <summary>"acknowledged", "awaiting_quiz", "open", "overdue", "withdrawn", "superseded" (stable keys for CSV and styling).</summary>
		public string StateKey { get; set; }
	}

	/// <summary>One protocol's line in the department compliance overview.</summary>
	public sealed class ProtocolComplianceSummary
	{
		public int ProtocolId { get; set; }

		public string Code { get; set; }

		public string Name { get; set; }

		public int Kind { get; set; }

		public int? VersionId { get; set; }

		public string VersionLabel { get; set; }

		public int Assigned { get; set; }

		public int Acknowledged { get; set; }

		public int Overdue { get; set; }

		public string StandardsTags { get; set; }

		/// <summary>Whole-number percentage acknowledged, honest (no rounding up to 100 until everyone has).</summary>
		public int Percent => Assigned == 0 ? 0 : Acknowledged >= Assigned ? 100 : (int)Math.Floor(Acknowledged * 100.0 / Assigned);
	}

	/// <summary>The compliance grid of one protocol version, or the department overview when no protocol is chosen.</summary>
	public sealed class ProtocolComplianceGrid
	{
		public DispatchProtocol Protocol { get; set; }

		public ProtocolVersion Version { get; set; }

		public List<ProtocolVersion> Versions { get; set; } = new List<ProtocolVersion>();

		public List<ProtocolComplianceRow> Rows { get; set; } = new List<ProtocolComplianceRow>();

		public List<ProtocolComplianceSummary> Summaries { get; set; } = new List<ProtocolComplianceSummary>();

		public int? GroupId { get; set; }
	}

	/// <summary>A priority bulletin to issue (R-P7).</summary>
	public sealed class ProtocolBulletinRequest
	{
		public string Title { get; set; }

		/// <summary>Rich text; sanitized for storage.</summary>
		public string ContentHtml { get; set; }

		public List<ContentAudienceRule> Audience { get; set; } = new List<ContentAudienceRule>();

		public DateTime ExpiresOnUtc { get; set; }

		/// <summary>Must-read: every member of the audience must acknowledge it (the card stays on home until they do).</summary>
		public bool MustRead { get; set; } = true;
	}

	/// <summary>A bulletin as the composer and the countersign queue list it.</summary>
	public sealed class ProtocolBulletinItem
	{
		public DispatchProtocol Protocol { get; set; }

		public ProtocolVersion Version { get; set; }

		public ProtocolVersionApproval Countersign { get; set; }

		public int Assigned { get; set; }

		public int Acknowledged { get; set; }

		public bool IsExpired { get; set; }
	}

	/// <summary>The LMS proof-of-compliance report: per standards tag, the protocols mapped to it and their acknowledgment state.</summary>
	public sealed class ProtocolStandardsReport
	{
		public DateTime GeneratedOn { get; set; }

		public List<ProtocolStandardsSection> Sections { get; set; } = new List<ProtocolStandardsSection>();
	}

	public sealed class ProtocolStandardsSection
	{
		public string Standard { get; set; }

		public List<ProtocolComplianceSummary> Protocols { get; set; } = new List<ProtocolComplianceSummary>();

		/// <summary>Signatures in the section (reviews and approvals) that verify against the content as stored now.</summary>
		public int VerifiedApprovals { get; set; }
	}
}
