using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Training T5 items 4 and 5 (protocols-training-knowledge-plan §13 T5, §13a "Learning Studio", §16.3, §17.2 row "AI"): course drafts from a
	// protocol version or an uploaded SOP (Enhanced AI and the LMS), flashcards with their source version and anchor (flagged for review when
	// the source is superseded), and the Resgrid starter course library cloned into a department with attribution (the LMS, no AI).

	/// <summary>Where a department course came from (one <see cref="LmsCourseOrigin"/> row per course that did not start blank).</summary>
	public enum LmsCourseOriginKinds
	{
		/// <summary>Drafted by Enhanced AI from a published protocol version: every lesson cites its sections.</summary>
		AiDraftFromProtocol = 0,

		/// <summary>Drafted by Enhanced AI from an uploaded SOP document's extracted text: every lesson cites its pages.</summary>
		AiDraftFromDocument = 1,

		/// <summary>Cloned from a Resgrid starter course (no AI), attribution kept.</summary>
		Starter = 2
	}

	/// <summary>
	/// The provenance and attribution of a department course (T5 item 4: "cloned into a department with attribution"; item 5: drafts cite their
	/// source). The attribution fields follow the protocol template library's convention (source key, name, version, URL, licence, attribution
	/// line) so both libraries read the same way. Written once when the course is created; the course's later versions keep it.
	/// </summary>
	[Table("LmsCourseOrigins")]
	public class LmsCourseOrigin : IEntity
	{
		public const int SourceKeyMaxLength = 200;
		public const int SourceNameMaxLength = 250;
		public const int SourceVersionMaxLength = 50;
		public const int SourceUrlMaxLength = 500;
		public const int LicenseMaxLength = 100;
		public const int AttributionMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseOriginId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		/// <summary>The first version the origin produced (the draft the author edits).</summary>
		public int LmsCourseVersionId { get; set; }

		/// <summary><see cref="LmsCourseOriginKinds"/>.</summary>
		public int OriginKind { get; set; }

		/// <summary>"starter:resgrid-onboarding", "protocol-version:12", "document:sop.pdf".</summary>
		[MaxLength(SourceKeyMaxLength)]
		public string SourceKey { get; set; }

		/// <summary>The source's title as it stood (the protocol's name, the file name, the starter's title).</summary>
		[MaxLength(SourceNameMaxLength)]
		public string SourceName { get; set; }

		/// <summary>The source's version label ("1.2") or the starter's revision.</summary>
		[MaxLength(SourceVersionMaxLength)]
		public string SourceVersion { get; set; }

		/// <summary>Where the source can be opened (a protocol version's page); null for an uploaded file or a starter.</summary>
		[MaxLength(SourceUrlMaxLength)]
		public string SourceUrl { get; set; }

		/// <summary>The licence key the content carries ("resgrid-starter", "department-own").</summary>
		[MaxLength(LicenseMaxLength)]
		public string License { get; set; }

		/// <summary>The attribution line shown on the course.</summary>
		[MaxLength(AttributionMaxLength)]
		public string Attribution { get; set; }

		/// <summary>The AiGenerations exchange that drafted the course (AI drafts only).</summary>
		public string ExchangeId { get; set; }

		/// <summary>Each drafted lesson's citations (<see cref="LmsLessonCitation"/> list as JSON); null for a starter.</summary>
		public string CitationsJson { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsAiDraft => OriginKind == (int)LmsCourseOriginKinds.AiDraftFromProtocol || OriginKind == (int)LmsCourseOriginKinds.AiDraftFromDocument;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseOriginId; }
			set { LmsCourseOriginId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseOrigins";

		[NotMapped]
		public string IdName => "LmsCourseOriginId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsAiDraft" };
	}

	/// <summary>The <see cref="LmsCourseOrigin.SourceKey"/> forms: "protocol:{id}/version:{versionId}", "document:{file name}", "starter:{key}".</summary>
	public static class LmsCourseOriginKeys
	{
		public const string ProtocolPrefix = "protocol:";
		public const string DocumentPrefix = "document:";

		public static string Protocol(int protocolId, int protocolVersionId) =>
			ProtocolPrefix + protocolId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/version:" + protocolVersionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public static string Document(string fileName)
		{
			var key = DocumentPrefix + (fileName ?? string.Empty).Trim();
			return key.Length <= LmsCourseOrigin.SourceKeyMaxLength ? key : key.Substring(0, LmsCourseOrigin.SourceKeyMaxLength);
		}

		/// <summary>The protocol (and version) a "protocol:{id}/version:{versionId}" key names.</summary>
		public static bool TryParseProtocol(string sourceKey, out int protocolId, out int protocolVersionId)
		{
			protocolId = 0;
			protocolVersionId = 0;
			if (string.IsNullOrWhiteSpace(sourceKey) || !sourceKey.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
				return false;
			var parts = sourceKey.Substring(ProtocolPrefix.Length).Split(new[] { "/version:" }, StringSplitOptions.None);
			return parts.Length == 2 &&
				int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out protocolId) && protocolId > 0 &&
				int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out protocolVersionId) && protocolVersionId > 0;
		}
	}

	/// <summary>One drafted lesson's citations: the sections (anchors) of its source it was written from.</summary>
	public sealed class LmsLessonCitation
	{
		public int LmsLessonId { get; set; }

		public string LessonTitle { get; set; }

		public List<string> Anchors { get; set; } = new List<string>();

		/// <summary>"Protocol name 1.2 · Section" or "file.pdf · p. 3", for each anchor in order.</summary>
		public List<string> Labels { get; set; } = new List<string>();

		public static List<LmsLessonCitation> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsLessonCitation>();
			try
			{
				return JsonConvert.DeserializeObject<List<LmsLessonCitation>>(json) ?? new List<LmsLessonCitation>();
			}
			catch (JsonException)
			{
				return new List<LmsLessonCitation>();
			}
		}
	}

	/// <summary>What a flashcard was drafted from.</summary>
	public enum LmsFlashcardSourceKinds
	{
		/// <summary>A published version of the course the card belongs to.</summary>
		CourseVersion = 0,

		/// <summary>A published protocol version (the course's source protocol, or one it embeds).</summary>
		ProtocolVersion = 1
	}

	public enum LmsFlashcardStatuses
	{
		/// <summary>Accepted by an author and shown to learners while its source version is in force.</summary>
		Active = 0,

		/// <summary>Its source version was superseded (or can no longer be read): hidden from learners until an author reviews it.</summary>
		NeedsReview = 1
	}

	public enum LmsFlashcardFlagReasons
	{
		None = 0,

		/// <summary>A newer version of the source went into force.</summary>
		SourceSuperseded = 1,

		/// <summary>The source version or its section can no longer be read (retired, removed, or marked "no AI").</summary>
		SourceUnavailable = 2
	}

	/// <summary>
	/// A practice flashcard of a course (T5 item 5, §16.4 "flashcards flagged for review when their source version changes"): AI-drafted, kept
	/// only when an author accepts it, and shown to the course's learners for spaced practice in the player. Never graded and never written as a
	/// learning record or attempt: practice state stays in the learner's browser. Each card records the source version and anchor it was drafted
	/// from; publishing a newer version of that source flags the card for review (<see cref="LmsFlashcardStatuses.NeedsReview"/>).
	/// </summary>
	[Table("LmsFlashcards")]
	public class LmsFlashcard : IEntity
	{
		public const int FrontMaxLength = 300;
		public const int BackMaxLength = 600;
		public const int AnchorMaxLength = 128;
		public const int HeadingMaxLength = 500;
		public const int TitleMaxLength = 250;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsFlashcardId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The course whose player shows the card.</summary>
		public int LmsCourseId { get; set; }

		/// <summary><see cref="LmsFlashcardSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>LmsCourseId or DispatchProtocolId of the source.</summary>
		public int SourceItemId { get; set; }

		/// <summary>LmsCourseVersionId or ProtocolVersionId the card was drafted from (or last reviewed against).</summary>
		public int SourceVersionId { get; set; }

		[MaxLength(50)]
		public string SourceVersionLabel { get; set; }

		/// <summary>The cited section's anchor (b-{key}, a heading anchor, l{lesson}-…).</summary>
		[MaxLength(AnchorMaxLength)]
		public string SourceAnchor { get; set; }

		[MaxLength(HeadingMaxLength)]
		public string SourceHeading { get; set; }

		[MaxLength(TitleMaxLength)]
		public string SourceTitle { get; set; }

		[MaxLength(FrontMaxLength)]
		public string Front { get; set; }

		[MaxLength(BackMaxLength)]
		public string Back { get; set; }

		/// <summary><see cref="LmsFlashcardStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="LmsFlashcardFlagReasons"/>.</summary>
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
		public bool IsActive => Status == (int)LmsFlashcardStatuses.Active;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsFlashcardId; }
			set { LmsFlashcardId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsFlashcards";

		[NotMapped]
		public string IdName => "LmsFlashcardId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>The outcome of a course-studio request (also AiGenerations.Outcome for the drafted ones).</summary>
	public static class LmsStudioOutcomes
	{
		public const string Drafted = "Drafted";
		public const string Created = "Created";

		/// <summary>Enhanced AI (knowledge answers) or the LMS add-on is missing: the AI tools are not shown at all (fails closed).</summary>
		public const string NotEntitled = "NotEntitled";

		/// <summary>The member may not author courses, or may not read the source.</summary>
		public const string NotAllowed = "NotAllowed";

		public const string NotFound = "NotFound";

		/// <summary>The source is marked "no AI" or is licensed content (decision 14): it is never sent to a model.</summary>
		public const string Excluded = "Excluded";

		/// <summary>Advanced Data Protection keeps the department's content local: no model call.</summary>
		public const string KeptLocal = "KeptLocal";

		/// <summary>The source has no readable text (an empty version, a scanned PDF).</summary>
		public const string NoContent = "NoContent";

		/// <summary>The operator's model is not configured, or the exchange could not be audited (fails closed).</summary>
		public const string Unconfigured = "Unconfigured";

		public const string Unavailable = "Unavailable";
		public const string Busy = "Busy";
		public const string BudgetExhausted = "BudgetExhausted";

		/// <summary>The model's output was refused (invalid, or nothing valid was left after the citation and number checks).</summary>
		public const string Refused = "Refused";

		public const string Invalid = "Invalid";

		/// <summary>The draft course could not be written (the course rules refused it); nothing was kept.</summary>
		public const string NotSaved = "NotSaved";
	}

	/// <summary>What a course draft is grounded in.</summary>
	public enum LmsCourseDraftSourceKinds
	{
		/// <summary>A published protocol version the member may read and author (its version in force, or the one named).</summary>
		ProtocolVersion = 0,

		/// <summary>An SOP document the author uploads (PDF, DOCX or plain text); only its extracted text is used and the file is not kept.</summary>
		Document = 1
	}

	/// <summary>A request to draft a course from a protocol version or an uploaded SOP.</summary>
	public sealed class LmsCourseDraftRequest
	{
		public const int MaxDocumentBytes = 10 * 1024 * 1024;
		public const int MinQuestions = 0;
		public const int MaxQuestions = 10;

		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }

		/// <summary><see cref="LmsCourseDraftSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		public int ProtocolId { get; set; }

		/// <summary>A published or superseded version of <see cref="ProtocolId"/>; null for the version in force.</summary>
		public int? ProtocolVersionId { get; set; }

		public string FileName { get; set; }
		public string ContentType { get; set; }
		public byte[] Data { get; set; }

		/// <summary>The course title the author typed; blank takes the drafted one.</summary>
		public string Title { get; set; }

		public int QuestionCount { get; set; } = 5;
	}

	/// <summary>The outcome of a course draft: the new draft course, or why there is none.</summary>
	public sealed class LmsCourseDraftResult
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public int LmsCourseId { get; set; }
		public int LmsCourseVersionId { get; set; }
		public int? LmsQuestionBankId { get; set; }
		public int Modules { get; set; }
		public int Lessons { get; set; }
		public int Questions { get; set; }

		/// <summary>Drafted items left out (a number the cited sections do not hold, an uncited lesson, a malformed item).</summary>
		public int Dropped { get; set; }

		/// <summary>The source was longer than one draft reads; only its first sections were used.</summary>
		public bool Truncated { get; set; }

		public string PromptVersion { get; set; }

		public bool Succeeded => Outcome == LmsStudioOutcomes.Created;
	}

	/// <summary>A request to draft flashcards for a course from one of its sources.</summary>
	public sealed class LmsFlashcardDraftRequest
	{
		public const int MinCards = 1;
		public const int MaxCards = 20;

		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }
		public int LmsCourseId { get; set; }

		/// <summary><see cref="LmsFlashcardSourceKinds"/>: the course's version in force, or a protocol's version in force.</summary>
		public int SourceKind { get; set; }

		/// <summary>The protocol (for <see cref="LmsFlashcardSourceKinds.ProtocolVersion"/>).</summary>
		public int ProtocolId { get; set; }

		public int Count { get; set; } = 10;
	}

	/// <summary>One drafted card as the author reviews it (nothing saved until accepted).</summary>
	public sealed class LmsFlashcardDraftCard
	{
		public string Front { get; set; }
		public string Back { get; set; }
		public string Anchor { get; set; }
		public string Heading { get; set; }
	}

	public sealed class LmsFlashcardDraft
	{
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public string SourceTitle { get; set; }
		public string SourceVersionLabel { get; set; }
		public List<LmsFlashcardDraftCard> Cards { get; set; } = new List<LmsFlashcardDraftCard>();
		public int Dropped { get; set; }
		public string PromptVersion { get; set; }
	}

	/// <summary>Whether the AI course tools are open to a member, and why not.</summary>
	public sealed class LmsStudioStatus
	{
		public bool AiAvailable { get; set; }
		public string AiReason { get; set; }

		/// <summary>The member may author courses (the LMS add-on, Manage Courses or admin): the starter library's clone button.</summary>
		public bool CanAuthor { get; set; }
	}

	/// <summary>A card as the course player shows it to a learner (front, back and where it comes from).</summary>
	public sealed class LmsFlashcardDeckCard
	{
		public int Id { get; set; }
		public string Front { get; set; }
		public string Back { get; set; }
		public string SourceTitle { get; set; }
		public string SourceVersionLabel { get; set; }
		public string SourceHeading { get; set; }

		/// <summary>The cited section's page (a protocol version's section, or the course lesson).</summary>
		public string SourceUrl { get; set; }
	}

	/// <summary>The outcome of an author's flashcard action.</summary>
	public sealed class LmsFlashcardResult
	{
		public string Outcome { get; set; }
		public int Count { get; set; }
		public LmsFlashcard Card { get; set; }

		public bool Succeeded => Outcome == LmsStudioOutcomes.Created;

		public static LmsFlashcardResult Fail(string outcome) => new LmsFlashcardResult { Outcome = outcome };
	}

	/// <summary>A course as the plan writer creates it: the shape both the AI draft and a starter clone produce.</summary>
	public sealed class LmsCoursePlan
	{
		public string Title { get; set; }
		public string Code { get; set; }
		public string Summary { get; set; }
		public string Tags { get; set; }
		public decimal Hours { get; set; }

		/// <summary>The version overview (sanitized on save).</summary>
		public string OverviewHtml { get; set; }

		public List<LmsCoursePlanModule> Modules { get; set; } = new List<LmsCoursePlanModule>();

		/// <summary>The question bank's name; null or no questions for none.</summary>
		public string BankName { get; set; }

		public string BankDescription { get; set; }

		public List<LmsCoursePlanQuestion> Questions { get; set; } = new List<LmsCoursePlanQuestion>();

		/// <summary>The module title of the closing check (an assessment lesson over the bank); null for no assessment lesson.</summary>
		public string CheckModuleTitle { get; set; }

		public string CheckLessonTitle { get; set; }

		public int CheckPassMark { get; set; } = 80;
	}

	public sealed class LmsCoursePlanModule
	{
		public string Title { get; set; }
		public List<LmsCoursePlanLesson> Lessons { get; set; } = new List<LmsCoursePlanLesson>();
	}

	public sealed class LmsCoursePlanLesson
	{
		public string Title { get; set; }

		/// <summary>A blocks lesson's html (sanitized on save).</summary>
		public string Html { get; set; }

		/// <summary>The source anchors the lesson cites (AI drafts).</summary>
		public List<string> Anchors { get; set; } = new List<string>();

		public List<string> CitationLabels { get; set; } = new List<string>();
	}

	public sealed class LmsCoursePlanQuestion
	{
		public string Text { get; set; }
		public List<string> Options { get; set; } = new List<string>();
		public int CorrectIndex { get; set; }
		public string Explanation { get; set; }

		/// <summary><see cref="LmsQuestionKinds.SingleChoice"/> or <see cref="LmsQuestionKinds.TrueFalse"/>.</summary>
		public int Kind { get; set; } = (int)LmsQuestionKinds.SingleChoice;
	}

	/// <summary>What the plan writer made.</summary>
	public sealed class LmsCoursePlanResult
	{
		public LmsErrors Error { get; set; }
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public int? LmsQuestionBankId { get; set; }
		public int Modules { get; set; }
		public int Lessons { get; set; }
		public int Questions { get; set; }

		/// <summary>The saved lesson id of each planned lesson, in plan order (modules then lessons).</summary>
		public List<int> LessonIds { get; set; } = new List<int>();

		public bool Succeeded => Error == LmsErrors.None && Course != null && Version != null;
	}

	/// <summary>
	/// The course validator (T5: a starter clone and an AI draft must pass it before they are offered to the author): a draft course version
	/// has a title; at least one module; every module a title and at least one lesson; every lesson a title and the content its kind needs
	/// (a blocks lesson readable text or a learner task, a video a source, a document an https link, a protocol lesson a version, an
	/// assessment a bank with at least one live question, a survey a question); hours in range. Returns the problems found (empty when valid).
	/// </summary>
	public static class LmsCourseValidator
	{
		public const string NoTitle = "course-title";
		public const string NoModules = "no-modules";
		public const string ModuleTitle = "module-title";
		public const string EmptyModule = "module-empty";
		public const string LessonTitle = "lesson-title";
		public const string LessonContent = "lesson-content";
		public const string AssessmentBank = "assessment-bank";
		public const string Hours = "hours";

		/// <param name="liveQuestionsByBank">How many live (not retired) questions each bank the outline's assessments use holds.</param>
		public static List<string> Validate(LmsCourseOutline outline, IReadOnlyDictionary<int, int> liveQuestionsByBank, Func<string, string> plainText)
		{
			var problems = new List<string>();
			if (outline?.Version == null)
			{
				problems.Add(NoModules);
				return problems;
			}

			if (string.IsNullOrWhiteSpace(outline.Version.Title) && string.IsNullOrWhiteSpace(outline.Course?.Title))
				problems.Add(NoTitle);
			if (outline.Version.Hours < 0 || outline.Version.Hours > 1000)
				problems.Add(Hours);

			var modules = outline.Modules ?? new List<LmsOutlineModule>();
			if (modules.Count == 0)
				problems.Add(NoModules);

			for (var m = 0; m < modules.Count; m++)
			{
				var module = modules[m];
				var label = (m + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
				if (string.IsNullOrWhiteSpace(module.Module?.Title))
					problems.Add(ModuleTitle + ":" + label);
				var lessons = module.Lessons ?? new List<LmsLesson>();
				if (lessons.Count == 0)
					problems.Add(EmptyModule + ":" + label);
				for (var l = 0; l < lessons.Count; l++)
				{
					var lesson = lessons[l];
					var where = label + "." + (l + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
					if (string.IsNullOrWhiteSpace(lesson.Title))
						problems.Add(LessonTitle + ":" + where);
					var problem = LessonProblem(lesson, liveQuestionsByBank, plainText);
					if (problem != null)
						problems.Add(problem + ":" + where);
				}
			}

			return problems;
		}

		private static string LessonProblem(LmsLesson lesson, IReadOnlyDictionary<int, int> banks, Func<string, string> plainText)
		{
			switch ((LmsLessonKinds)lesson.Kind)
			{
				case LmsLessonKinds.Blocks:
					var text = plainText?.Invoke(lesson.ContentHtml ?? string.Empty) ?? lesson.ContentHtml;
					return string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(lesson.FreeResponsePrompt) && string.IsNullOrWhiteSpace(lesson.FileRequestPrompt)
						? LessonContent : null;
				case LmsLessonKinds.Video:
					return string.IsNullOrWhiteSpace(lesson.VideoUrl) && !lesson.LmsMediaAssetId.HasValue ? LessonContent : null;
				case LmsLessonKinds.Document:
					return Uri.TryCreate(lesson.DocumentUrl ?? string.Empty, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? null : LessonContent;
				case LmsLessonKinds.ProtocolVersion:
					return lesson.ProtocolVersionId.HasValue ? null : LessonContent;
				case LmsLessonKinds.Assessment:
					return lesson.LmsQuestionBankId.HasValue && banks != null && banks.TryGetValue(lesson.LmsQuestionBankId.Value, out var live) && live > 0 ? null : AssessmentBank;
				case LmsLessonKinds.Survey:
					return LmsSurveyQuestion.Parse(lesson.SurveyJson).Any(q => !string.IsNullOrWhiteSpace(q.Text)) ? null : LessonContent;
				default:
					return LessonContent;
			}
		}
	}
}
