using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	// Enhanced AI on protocols (round 7A, product decision 2026-11-25: Enhanced AI pays for every model call; the LMS is needed only for content
	// that exists only in the LMS). Baseline types: protocol flashcards, a baseline Training drafted from a protocol version, and translation
	// drafts of a protocol version. All need only the Enhanced AI add-on with knowledge answers (Ai.Knowledge); none references an LMS type.

	/// <summary>A protocol flashcard's state (ProtocolFlashcards.Status). Persisted: append only.</summary>
	public enum ProtocolFlashcardStatuses
	{
		/// <summary>Shown to members who may read the protocol while its source version is in force.</summary>
		Active = 0,

		/// <summary>Hidden from members until an author checks it against the version in force.</summary>
		NeedsReview = 1
	}

	/// <summary>Why a protocol flashcard needs review. Persisted: append only.</summary>
	public enum ProtocolFlashcardFlagReasons
	{
		None = 0,

		/// <summary>A newer version of the protocol went into force.</summary>
		SourceSuperseded = 1,

		/// <summary>The source version or its section can no longer be read (retired, or marked "no AI").</summary>
		SourceUnavailable = 2
	}

	/// <summary>
	/// A practice flashcard of a protocol (round 7A; §16.4 "flashcards flagged for review when their source version changes"): AI-drafted from a
	/// published protocol version, kept only when a protocol author accepts it, and shown on the protocol to members who may read it. Never graded
	/// and never written as a learning record or attempt: practice state stays in the member's browser. Each card cites one section (block anchor)
	/// of the version it was drafted from; a newer version going into force flags it <see cref="ProtocolFlashcardStatuses.NeedsReview"/>.
	/// Department-owned with no foreign keys (ProtocolDepartmentCleanup clears it).
	/// </summary>
	[Table("ProtocolFlashcards")]
	public class ProtocolFlashcard : IEntity
	{
		public const int FrontMaxLength = 300;
		public const int BackMaxLength = 600;
		public const int AnchorMaxLength = 128;
		public const int HeadingMaxLength = 500;
		public const int VersionLabelMaxLength = 50;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolFlashcardId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>The protocol version the card was drafted from (or last reviewed against).</summary>
		public int ProtocolVersionId { get; set; }

		[MaxLength(VersionLabelMaxLength)]
		public string SourceVersionLabel { get; set; }

		/// <summary>The cited section's anchor (b-{key}, a heading anchor, or a page of a document-backed version).</summary>
		[MaxLength(AnchorMaxLength)]
		public string SourceAnchor { get; set; }

		[MaxLength(HeadingMaxLength)]
		public string SourceHeading { get; set; }

		[MaxLength(FrontMaxLength)]
		public string Front { get; set; }

		[MaxLength(BackMaxLength)]
		public string Back { get; set; }

		/// <summary><see cref="ProtocolFlashcardStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="ProtocolFlashcardFlagReasons"/>.</summary>
		public int FlagReason { get; set; }

		public DateTime? FlaggedOn { get; set; }

		public int SortOrder { get; set; }

		/// <summary>The AiGenerations exchange the card was drafted in.</summary>
		public string ExchangeId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? ReviewedOn { get; set; }

		public string ReviewedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsActive => Status == (int)ProtocolFlashcardStatuses.Active;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolFlashcardId; }
			set { ProtocolFlashcardId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolFlashcards";

		[NotMapped]
		public string IdName => "ProtocolFlashcardId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>The outcome of a protocol AI tool request (also AiGenerations.Outcome for the drafted ones).</summary>
	public static class ProtocolAiOutcomes
	{
		public const string Drafted = "Drafted";
		public const string Created = "Created";

		/// <summary>Enhanced AI with knowledge answers is missing: the tools are not shown at all (fails closed).</summary>
		public const string NotEntitled = "NotEntitled";

		/// <summary>The member may not author the protocol (or create trainings, for a training draft).</summary>
		public const string NotAllowed = "NotAllowed";

		public const string NotFound = "NotFound";

		/// <summary>The protocol is marked "no AI" or is licensed content (decision 14): it is never sent to a model.</summary>
		public const string Excluded = "Excluded";

		/// <summary>Advanced Data Protection keeps the department's content local: no model call.</summary>
		public const string KeptLocal = "KeptLocal";

		/// <summary>The version has no readable text (an empty version, a scanned PDF), or nothing is left to translate.</summary>
		public const string NoContent = "NoContent";

		/// <summary>The operator's model is not configured, or the exchange could not be audited (fails closed).</summary>
		public const string Unconfigured = "Unconfigured";

		public const string Unavailable = "Unavailable";
		public const string Busy = "Busy";
		public const string BudgetExhausted = "BudgetExhausted";

		/// <summary>The model's output was refused (invalid, or nothing valid was left after the citation and number checks).</summary>
		public const string Refused = "Refused";

		/// <summary>The source changed since the draft was written (a newer version, an edited section): draft again.</summary>
		public const string Stale = "Stale";

		public const string Invalid = "Invalid";

		/// <summary>The drafted training could not be written; nothing was kept.</summary>
		public const string NotSaved = "NotSaved";

		/// <summary>Only a draft translation of a locked version is drafted into.</summary>
		public const string NotDraft = "NotDraft";
	}

	/// <summary>Whether a member has the protocol AI tools right now (Enhanced AI with knowledge answers, the operator's model, not kept local).</summary>
	public sealed class ProtocolAiStatus
	{
		public bool Available { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>A request to draft flashcards from a protocol's version in force.</summary>
	public sealed class ProtocolFlashcardDraftRequest
	{
		public const int MinCards = 1;
		public const int MaxCards = 20;

		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }
		public int ProtocolId { get; set; }
		public int Count { get; set; } = 10;
	}

	/// <summary>One drafted card as the author reviews it (nothing saved until accepted).</summary>
	public sealed class ProtocolFlashcardDraftCard
	{
		public string Front { get; set; }
		public string Back { get; set; }
		public string Anchor { get; set; }
		public string Heading { get; set; }
	}

	public sealed class ProtocolFlashcardDraft
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public string SourceTitle { get; set; }
		public string SourceVersionLabel { get; set; }
		public List<ProtocolFlashcardDraftCard> Cards { get; set; } = new List<ProtocolFlashcardDraftCard>();
		public int Dropped { get; set; }
		public string PromptVersion { get; set; }
	}

	/// <summary>The outcome of an author's flashcard action.</summary>
	public sealed class ProtocolFlashcardResult
	{
		public string Outcome { get; set; }
		public int Count { get; set; }
		public ProtocolFlashcard Card { get; set; }

		public bool Succeeded => Outcome == ProtocolAiOutcomes.Created;

		public static ProtocolFlashcardResult Fail(string outcome) => new ProtocolFlashcardResult { Outcome = outcome };
	}

	/// <summary>A card as the protocol page shows it to a member (front, back and the section it comes from).</summary>
	public sealed class ProtocolFlashcardDeckCard
	{
		public int Id { get; set; }
		public string Front { get; set; }
		public string Back { get; set; }
		public string SourceVersionLabel { get; set; }
		public string SourceHeading { get; set; }

		/// <summary>The cited section's web path (the protocol version's section).</summary>
		public string SourceUrl { get; set; }
	}

	/// <summary>A request to draft a baseline Training (text and quiz) from a protocol's version in force.</summary>
	public sealed class ProtocolTrainingDraftRequest
	{
		public const int MinQuestions = 0;
		public const int MaxQuestions = 10;

		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }
		public int ProtocolId { get; set; }
		public int QuestionCount { get; set; } = 5;
	}

	/// <summary>One cited section as a drafted item names it.</summary>
	public sealed class ProtocolAiCitation
	{
		public string Anchor { get; set; }
		public string Label { get; set; }

		/// <summary>The section's web path in the cited version.</summary>
		public string Url { get; set; }
	}

	/// <summary>One drafted part of the training text: the model's introduction and key points, then the cited sections' own text.</summary>
	public sealed class ProtocolTrainingDraftSection
	{
		public string Title { get; set; }
		public string Intro { get; set; }
		public List<string> KeyPoints { get; set; } = new List<string>();
		public List<ProtocolAiCitation> Citations { get; set; } = new List<ProtocolAiCitation>();
	}

	/// <summary>One drafted quiz question: single choice, one correct option, an explanation, and the sections it is asked from.</summary>
	public sealed class ProtocolTrainingDraftQuestion
	{
		public string Text { get; set; }
		public List<string> Options { get; set; } = new List<string>();
		public int CorrectIndex { get; set; }
		public string Explanation { get; set; }
		public List<ProtocolAiCitation> Citations { get; set; } = new List<ProtocolAiCitation>();
	}

	/// <summary>A drafted training as the author reviews it before anything is created.</summary>
	public sealed class ProtocolTrainingDraft
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public string SourceTitle { get; set; }
		public string SourceVersionLabel { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public List<ProtocolTrainingDraftSection> Sections { get; set; } = new List<ProtocolTrainingDraftSection>();
		public List<ProtocolTrainingDraftQuestion> Questions { get; set; } = new List<ProtocolTrainingDraftQuestion>();

		/// <summary>Drafted items left out (a number the cited sections do not hold, an uncited section, a malformed item).</summary>
		public int Dropped { get; set; }

		/// <summary>The version was longer than one draft reads; only its first sections were used.</summary>
		public bool Truncated { get; set; }

		public string PromptVersion { get; set; }
	}

	/// <summary>The outcome of creating the draft training an author accepted.</summary>
	public sealed class ProtocolTrainingDraftResult
	{
		public string Outcome { get; set; }
		public int TrainingId { get; set; }
		public int Questions { get; set; }

		public bool Succeeded => Outcome == ProtocolAiOutcomes.Created;

		public static ProtocolTrainingDraftResult Fail(string outcome) => new ProtocolTrainingDraftResult { Outcome = outcome };
	}

	/// <summary>
	/// An AI translation draft of a protocol version into a draft translation's language: the translation's document with the fields that were
	/// still in the source wording replaced by validated translations. Nothing is saved: the editor shows it and the author saves the draft,
	/// which still needs its own L2 approval.
	/// </summary>
	public sealed class ProtocolTranslationDraft
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }

		/// <summary>The translation's document with the drafted wording (aligned to the source: structure, doses and links are the source's).</summary>
		public ProtocolContentDocument Document { get; set; }

		/// <summary>Fields drafted and kept.</summary>
		public int Translated { get; set; }

		/// <summary>Drafted fields refused (a number changed, added or removed, the markup or a link changed, too long); they keep their wording.</summary>
		public int Dropped { get; set; }

		/// <summary>Fields still in the source wording that one draft did not reach (run it again).</summary>
		public int Remaining { get; set; }

		public string PromptVersion { get; set; }
	}
}
