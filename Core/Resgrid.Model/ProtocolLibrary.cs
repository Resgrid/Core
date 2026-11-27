using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What a protocol is (DispatchProtocols.Kind, P1). Persisted: append only, never renumber.</summary>
	public enum ProtocolKinds
	{
		/// <summary>A dispatch protocol: matched to calls by its triggers, with key questions. Every protocol before P1.</summary>
		Dispatch = 0,

		/// <summary>A standard operating procedure or guideline (SOP/SOG).</summary>
		Sog = 1,

		ClinicalGuideline = 2,

		Policy = 3,

		/// <summary>A procedure or checklist; like Dispatch, its triggers can suggest it on a call.</summary>
		Procedure = 4,

		Reference = 5,

		/// <summary>
		/// A priority bulletin (P2, R-P7): short must-read content issued straight into force by a holder of the publish right,
		/// approved afterwards (countersigned or withdrawn), retired by itself at its expiry. Never created from the kind pickers.
		/// </summary>
		Bulletin = 6
	}

	/// <summary>
	/// Where a protocol stands as a whole (DispatchProtocols.Status). Persisted. Whether a new version is in progress is read from
	/// its versions; this says whether members have something in force to read.
	/// </summary>
	public enum ProtocolStatuses
	{
		/// <summary>Never published: only authors and approvers see it.</summary>
		Draft = 0,

		/// <summary>A published version is in force (<see cref="DispatchProtocol.CurrentVersionId"/>).</summary>
		Published = 1,

		/// <summary>Retired or expired: kept for the calls and the history that cite it, no longer in force.</summary>
		Retired = 2
	}

	/// <summary>One version's place in the lifecycle (ProtocolVersions.Status). Persisted: append only.</summary>
	public enum ProtocolVersionStatuses
	{
		/// <summary>The working copy: the only status whose content can change.</summary>
		Draft = 0,

		/// <summary>Submitted; its approval steps are open. The content is locked.</summary>
		InReview = 1,

		/// <summary>Every step approved; waiting to be published now or on its effective date (worker 84).</summary>
		Approved = 2,

		/// <summary>In force. At most one per protocol.</summary>
		Published = 3,

		/// <summary>Replaced by a later published version.</summary>
		Superseded = 4,

		/// <summary>Withdrawn or expired (past ExpiresOn) without a replacement.</summary>
		Retired = 5,

		/// <summary>Text a call was dispatched with before versions existed (R-P2), visible only from the calls that cite it.</summary>
		Legacy = 6
	}

	public enum ProtocolApprovalDecisions
	{
		Pending = 0,
		Approved = 1,
		Rejected = 2,
		/// <summary>The version went back to draft (another step rejected it, or its author withdrew it) before this step decided.</summary>
		Withdrawn = 3
	}

	/// <summary>ProtocolComments.Kind. Persisted: append only.</summary>
	public enum ProtocolCommentKinds
	{
		/// <summary>A reviewer's comment on a version, anchored to a heading or block; resolved before approval.</summary>
		ReviewComment = 0,

		/// <summary>A member suggesting a change from the reading view; reaches the owner as a thread on the next draft.</summary>
		Suggestion = 1,

		/// <summary>A member asking a question from the reading view.</summary>
		Question = 2,

		/// <summary>A reply in a thread.</summary>
		Reply = 3
	}

	/// <summary>The per-kind defaults (P1): triggers and acknowledgment.</summary>
	public static class ProtocolKindRules
	{
		/// <summary>Triggers (and so call matching) only mean something for Dispatch and Procedure protocols.</summary>
		public static bool TriggersApply(int kind) => kind == (int)ProtocolKinds.Dispatch || kind == (int)ProtocolKinds.Procedure;

		/// <summary>Acknowledgment is on by default for Policy and SOG versions.</summary>
		public static bool AcknowledgmentByDefault(int kind) => kind == (int)ProtocolKinds.Policy || kind == (int)ProtocolKinds.Sog;

		public static bool IsKnown(int kind) => Enum.IsDefined(typeof(ProtocolKinds), kind);

		/// <summary>A kind an author can pick for a library protocol (every kind but Bulletin, which only the bulletin composer issues).</summary>
		public static bool IsAuthorable(int kind) => IsKnown(kind) && kind != (int)ProtocolKinds.Bulletin;

		/// <summary>The kinds offered by the library's pickers, in order.</summary>
		public static IReadOnlyList<ProtocolKinds> AuthorableKinds { get; } = Enum.GetValues(typeof(ProtocolKinds)).Cast<ProtocolKinds>()
			.Where(k => k != ProtocolKinds.Bulletin).ToList();
	}

	/// <summary>A library category (P1): nested, per kind, ordered, with optional approval steps of its own.</summary>
	[Table("ProtocolCategories")]
	public class ProtocolCategory : IEntity
	{
		public const int NameMaxLength = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolCategoryId { get; set; }

		public int DepartmentId { get; set; }

		public int? ParentCategoryId { get; set; }

		[MaxLength(NameMaxLength)]
		public string Name { get; set; }

		/// <summary>The kind the category is for (<see cref="ProtocolKinds"/>); null for any kind.</summary>
		public int? Kind { get; set; }

		public int SortOrder { get; set; }

		/// <summary>
		/// The category's approval steps (<see cref="ProtocolApprovalStep"/> list as JSON). Empty: one step, approved by any holder
		/// of Approve Protocols.
		/// </summary>
		public string ApprovalStepsJson { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		public List<ProtocolApprovalStep> ApprovalSteps
		{
			get => ProtocolApprovalStep.Parse(ApprovalStepsJson);
			set => ApprovalStepsJson = value == null || value.Count == 0 ? null : JsonConvert.SerializeObject(value);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolCategoryId; }
			set { ProtocolCategoryId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolCategories";

		[NotMapped]
		public string IdName => "ProtocolCategoryId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ApprovalSteps" };
	}

	/// <summary>One approval step of a category: a named approver, or any member of a role who holds Approve Protocols.</summary>
	public sealed class ProtocolApprovalStep
	{
		public int Order { get; set; }

		public string ApproverUserId { get; set; }

		public int? ApproverRoleId { get; set; }

		public static List<ProtocolApprovalStep> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<ProtocolApprovalStep>();
			try
			{
				return (JsonConvert.DeserializeObject<List<ProtocolApprovalStep>>(json) ?? new List<ProtocolApprovalStep>())
					.Where(x => x != null && (!string.IsNullOrWhiteSpace(x.ApproverUserId) || x.ApproverRoleId.HasValue))
					.OrderBy(x => x.Order)
					.ToList();
			}
			catch (JsonException)
			{
				return new List<ProtocolApprovalStep>();
			}
		}
	}

	/// <summary>
	/// One version of a protocol's content (P1). Immutable once it leaves Draft: only its status and dates move afterwards.
	/// <see cref="ContentJson"/> holds the blocks (<see cref="ProtocolContentDocument"/>: one <c>html</c> block in P1; P3 adds block
	/// types without a migration), <see cref="ContentHtml"/> their sanitized render and <see cref="ContentHash"/>
	/// <see cref="ProtocolContentHash.Compute"/> of it, the same hash the P0 call snapshots carry.
	/// </summary>
	[Table("ProtocolVersions")]
	public class ProtocolVersion : IEntity
	{
		public const int ChangeSummaryMaxLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolVersionId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>0 until published (a draft has no number yet); legacy versions are 0.n.</summary>
		public int Major { get; set; }

		public int Minor { get; set; }

		/// <summary><see cref="ProtocolVersionStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>When the version takes force: set at publish (now, or a later date worker 84 activates).</summary>
		public DateTime? EffectiveOn { get; set; }

		/// <summary>When the version stops being in force (worker 84 retires it); null for no end.</summary>
		public DateTime? ExpiresOn { get; set; }

		public string ContentJson { get; set; }

		public string ContentHtml { get; set; }

		[MaxLength(64)]
		public string ContentHash { get; set; }

		/// <summary>What changed and why; required on publish.</summary>
		public string ChangeSummary { get; set; }

		public bool RequiresAcknowledgment { get; set; }

		/// <summary>The content's language (BCP 47, e.g. "en"); null for the department's.</summary>
		public string Locale { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }

		/// <summary>Last saved (draft), for the "changed by … at …" refusal.</summary>
		public DateTime? UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }

		public DateTime? SubmittedOn { get; set; }
		public string SubmittedByUserId { get; set; }

		public DateTime? ApprovedOn { get; set; }
		public string ApprovedByUserId { get; set; }

		public DateTime? PublishedOn { get; set; }
		public string PublishedByUserId { get; set; }

		public DateTime? SupersededOn { get; set; }

		public DateTime? RetiredOn { get; set; }

		/// <summary>Bumped on every write; a save against an older value is refused (R-P8).</summary>
		public long RowVersion { get; set; }

		/// <summary>
		/// <see cref="ProtocolVersionContentKinds"/> (M0347): authored HTML blocks, or an uploaded document (one primary PDF or DOCX in
		/// ProtocolVersionFiles) whose <see cref="ContentHash"/> is the SHA-256 of the file's bytes.
		/// </summary>
		public int ContentKind { get; set; }

		/// <summary>
		/// The baseline Training whose quiz gates acknowledgment of this version (P2, free): frozen from the protocol's
		/// AckTrainingId when the version is published; the acknowledgment completes only when that training is passed.
		/// </summary>
		public int? QuizTrainingId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsDocument => ContentKind == (int)ProtocolVersionContentKinds.Document;

		[NotMapped]
		[JsonIgnore]
		public string Label => Status == (int)ProtocolVersionStatuses.Draft || Status == (int)ProtocolVersionStatuses.InReview || (Status == (int)ProtocolVersionStatuses.Approved && Major == 0)
			? null
			: Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolVersionId; }
			set { ProtocolVersionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolVersions";

		[NotMapped]
		public string IdName => "ProtocolVersionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Label", "IsDocument" };
	}

	/// <summary>One approval step of one submission of a version (P1), with its L2 signature bound to the content hash.</summary>
	[Table("ProtocolVersionApprovals")]
	public class ProtocolVersionApproval : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolVersionApprovalId { get; set; }

		public int DepartmentId { get; set; }

		public int ProtocolVersionId { get; set; }

		/// <summary>Which submission of the version this step belongs to (a rejected version resubmitted gets new steps).</summary>
		public int Round { get; set; }

		public int StepOrder { get; set; }

		/// <summary>A named approver; null when any holder of Approve Protocols (in <see cref="ApproverRoleId"/> when set) may decide.</summary>
		public string ApproverUserId { get; set; }

		public int? ApproverRoleId { get; set; }

		/// <summary><see cref="ProtocolApprovalDecisions"/>.</summary>
		public int Decision { get; set; }

		public string DecidedByUserId { get; set; }

		public string Comment { get; set; }

		public DateTime? DecidedOn { get; set; }

		/// <summary>The L2 signature an approval carries (SignatureRecords).</summary>
		public int? SignatureRecordId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolVersionApprovalId; }
			set { ProtocolVersionApprovalId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolVersionApprovals";

		[NotMapped]
		public string IdName => "ProtocolVersionApprovalId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Who a protocol is for (P1; the shared <see cref="ContentAudienceKinds"/> incl. Shift). A protocol without rows is for the
	/// whole department. Enforced on every read path (R-P4) by <see cref="Services.IProtocolAccessService"/>.
	/// </summary>
	[Table("ProtocolAudiences")]
	public class ProtocolAudience : IEntity, IContentAudienceRule
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolAudienceId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary><see cref="ContentAudienceKinds"/>.</summary>
		public int Kind { get; set; }

		public string RefId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolAudienceId; }
			set { ProtocolAudienceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolAudiences";

		[NotMapped]
		public string IdName => "ProtocolAudienceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A review comment anchored to a heading or block, or a member's suggestion or question from the reading view, and their
	/// replies (R-P8). Never hard-deleted by a user; resolved instead.
	/// </summary>
	[Table("ProtocolComments")]
	public class ProtocolComment : IEntity
	{
		public const int BodyMaxLength = 4000;
		public const int AnchorMaxLength = 128;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolCommentId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>The version commented on (review comments); for a suggestion, the version the member was reading.</summary>
		public int? ProtocolVersionId { get; set; }

		public int? ParentCommentId { get; set; }

		/// <summary><see cref="ProtocolCommentKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The heading anchor or block key the comment is about; null for the whole version.</summary>
		[MaxLength(AnchorMaxLength)]
		public string Anchor { get; set; }

		public string Body { get; set; }

		public string AuthorUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ResolvedOn { get; set; }

		public string ResolvedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsResolved => ResolvedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolCommentId; }
			set { ProtocolCommentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolComments";

		[NotMapped]
		public string IdName => "ProtocolCommentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsResolved" };
	}

	/// <summary>Why the lifecycle refused a step. Each maps to a localized message.</summary>
	public enum ProtocolLifecycleErrors
	{
		None = 0,
		NotFound,
		NotAuthorized,
		NotDraft,
		NotInReview,
		NotApproved,
		/// <summary>A save against an older copy of the draft (R-P8).</summary>
		StaleCopy,
		/// <summary>Review comments are still open on the version.</summary>
		OpenComments,
		ChangeSummaryRequired,
		/// <summary>The approval needs an L2 signature (a fresh step-up or a hash-bound passkey).</summary>
		SignatureRequired,
		/// <summary>The department requires a passkey for this signature.</summary>
		PasskeyRequired,
		/// <summary>The content changed after the step was signed (the hash no longer matches).</summary>
		ArtifactChanged,
		/// <summary>The caller already decided an earlier step of this submission (no one approves twice).</summary>
		AlreadyDecided,
		/// <summary>Nothing published to review.</summary>
		NotPublished,
		DraftExists,
		EmptyContent,
		EffectiveDateInPast,
		RetentionProtected,
		InvalidInput,
		/// <summary>A document-backed version needs its primary file (P1 R-P5).</summary>
		FileRequired,
		/// <summary>Only PDF and DOCX files can be a protocol's content.</summary>
		FileTypeNotAllowed,
		/// <summary>The file is larger than the per-file cap (decision 9b).</summary>
		FileTooLarge,
		/// <summary>P4: the decision graph has problems (no single first step, a path without an outcome, a loop…); fix it before review.</summary>
		DecisionGraphInvalid,
		/// <summary>P4: a decision graph belongs to an authored Dispatch protocol (not another kind, not a document-backed version).</summary>
		DecisionGraphNotAllowed,
		/// <summary>
		/// Round 10 A3: the protocol is a copy managed by a subscribed protocol set; its content changes only through the set's update inbox and
		/// customize page (unsubscribing makes it the department's own).
		/// </summary>
		ManagedBySet
	}

	/// <summary>The lifecycle rules (P1 §5): which status may move to which, and when content may change.</summary>
	public static class ProtocolLifecycle
	{
		/// <summary>Only a draft's content may change; every other status is immutable (published versions above all).</summary>
		public static bool IsContentEditable(int status) => status == (int)ProtocolVersionStatuses.Draft;

		/// <summary>The allowed transitions. Anything else is refused.</summary>
		public static bool CanMove(int from, int to)
		{
			switch ((ProtocolVersionStatuses)from)
			{
				case ProtocolVersionStatuses.Draft:
					return to == (int)ProtocolVersionStatuses.InReview;
				case ProtocolVersionStatuses.InReview:
					return to == (int)ProtocolVersionStatuses.Approved || to == (int)ProtocolVersionStatuses.Draft;
				case ProtocolVersionStatuses.Approved:
					return to == (int)ProtocolVersionStatuses.Published || to == (int)ProtocolVersionStatuses.Draft;
				case ProtocolVersionStatuses.Published:
					return to == (int)ProtocolVersionStatuses.Superseded || to == (int)ProtocolVersionStatuses.Retired;
				default:
					// Superseded, Retired and Legacy are final.
					return false;
			}
		}

		/// <summary>The number a version gets when it is published.</summary>
		public static (int Major, int Minor) NextNumber(ProtocolVersion currentPublished, bool major) =>
			NextNumber(currentPublished?.Major ?? 0, currentPublished?.Minor ?? 0, major);

		/// <summary>
		/// The number a version of any content-core domain gets when it is published (courses, T2), from the last published
		/// number: 1.0 first, then major (n+1.0) or minor (n.m+1).
		/// </summary>
		public static (int Major, int Minor) NextNumber(int currentMajor, int currentMinor, bool major)
		{
			if (currentMajor <= 0)
				return (1, 0);
			return major ? (currentMajor + 1, 0) : (currentMajor, currentMinor + 1);
		}

		/// <summary>Versions a user action may never remove (R-P10): everything that was ever submitted or published.</summary>
		public static bool IsRetained(int status) => status != (int)ProtocolVersionStatuses.Draft;

		/// <summary>The next review date from a review or publish on <paramref name="fromUtc"/>; null when there is no interval.</summary>
		public static DateTime? NextReview(DateTime fromUtc, int? intervalDays) =>
			intervalDays.HasValue && intervalDays.Value > 0 ? fromUtc.AddDays(intervalDays.Value) : (DateTime?)null;
	}

	/// <summary>The review reminder stages worker 84 has sent for the current NextReviewOn (DispatchProtocols.ReviewReminderStage).</summary>
	public enum ProtocolReviewReminderStages
	{
		None = 0,
		ThirtyDays = 1,
		SevenDays = 2,
		Overdue = 3
	}

	public static class ProtocolReviewReminders
	{
		/// <summary>The stage due on <paramref name="utcNow"/> for a review due on <paramref name="nextReviewOn"/>.</summary>
		public static ProtocolReviewReminderStages DueStage(DateTime nextReviewOn, DateTime utcNow)
		{
			if (utcNow >= nextReviewOn)
				return ProtocolReviewReminderStages.Overdue;
			if (utcNow >= nextReviewOn.AddDays(-7))
				return ProtocolReviewReminderStages.SevenDays;
			if (utcNow >= nextReviewOn.AddDays(-30))
				return ProtocolReviewReminderStages.ThirtyDays;
			return ProtocolReviewReminderStages.None;
		}
	}
}
