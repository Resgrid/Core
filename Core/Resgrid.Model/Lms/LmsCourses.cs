using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// A course (T2, plan §12.3) on the shared content core (§12.1, decision 8): the identity a department (or the system
	/// publisher) keeps across versions. Everything a learner takes lives on a <see cref="LmsCourseVersion"/>; the course row
	/// holds the catalog fields and which version is in force. LMS tables reference baseline tables, never the reverse (§17.3).
	/// </summary>
	[Table("LmsCourses")]
	public class LmsCourse : IEntity
	{
		public const int TitleMaxLength = 250;
		public const int SummaryMaxLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseId { get; set; }

		/// <summary>The owning department; 0 for system-published content (<see cref="LmsPublisherKinds.System"/>).</summary>
		public int DepartmentId { get; set; }

		/// <summary><see cref="LmsPublisherKinds"/>.</summary>
		public int PublisherKind { get; set; }

		/// <summary>The system publisher's stable key, e.g. "academy:05-calls" (unique); null for department courses.</summary>
		public string SystemKey { get; set; }

		/// <summary>The Academy manifest version the system course was last seeded from (re-seeding publishes when it rises).</summary>
		public int ManifestVersion { get; set; }

		[MaxLength(TitleMaxLength)]
		public string Title { get; set; }

		/// <summary>A department's own course code (shown on the transcript as the course code).</summary>
		public string Code { get; set; }

		public string Summary { get; set; }

		/// <summary>Comma separated tags.</summary>
		public string Tags { get; set; }

		/// <summary>The course's state as a whole, the content core's <see cref="ProtocolStatuses"/> (Draft, Published, Retired).</summary>
		public int Status { get; set; }

		/// <summary>The version in force (<see cref="ProtocolVersionStatuses.Published"/>); null until first published.</summary>
		public int? CurrentVersionId { get; set; }

		/// <summary>Members may enrol themselves from the catalog (when they are in the audience).</summary>
		public bool IsSelfEnrollable { get; set; }

		/// <summary>
		/// Entitlement keys the course needs to be visible (Academy, R-S2 "visibility follows entitlement"): comma separated
		/// "module:Records", "flag:Records.System", "addon:EnhancedAi". Empty for everyone.
		/// </summary>
		public string VisibilityKeys { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		/// <summary>Bumped on every write of the course row; a save against an older value is refused.</summary>
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsSystem => PublisherKind == (int)LmsPublisherKinds.System;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseId; }
			set { LmsCourseId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourses";

		[NotMapped]
		public string IdName => "LmsCourseId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsSystem" };
	}

	/// <summary>
	/// One version of a course (the content core's version shape, shared with <see cref="ProtocolVersion"/>): status from
	/// <see cref="ProtocolVersionStatuses"/> moved only by <see cref="ProtocolLifecycle"/>, an overview in
	/// <see cref="ContentJson"/>/<see cref="ContentHtml"/>, and <see cref="ContentHash"/> over the canonical version
	/// (settings, modules, lessons; <see cref="LmsCourseContent.ComputeHash"/>) that every approval signs at L2. Immutable once
	/// it leaves Draft. A learner finishes the version they started (R-L12).
	/// </summary>
	[Table("LmsCourseVersions")]
	public class LmsCourseVersion : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseVersionId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		public int Major { get; set; }

		public int Minor { get; set; }

		/// <summary><see cref="ProtocolVersionStatuses"/> (the shared lifecycle).</summary>
		public int Status { get; set; }

		/// <summary>The version's title (the course title when it was drafted).</summary>
		public string Title { get; set; }

		public string ContentJson { get; set; }

		public string ContentHtml { get; set; }

		[MaxLength(64)]
		public string ContentHash { get; set; }

		public string ChangeSummary { get; set; }

		public string Locale { get; set; }

		/// <summary>Hours credited on completion (the learning record's hours).</summary>
		public decimal Hours { get; set; }

		/// <summary>The department's TrainingCategories row credited (baseline table); null for none.</summary>
		public int? CategoryId { get; set; }

		/// <summary>The CE provider and course number the department entered (§14: Resgrid is not an accreditor).</summary>
		public string CeProvider { get; set; }

		public string CeCourseNumber { get; set; }

		/// <summary>The custom certificate template (LmsCertificateTemplates, T1) used for the completion certificate.</summary>
		public int? CertificateTemplateId { get; set; }

		/// <summary>Completion issues or renews this certification type (DepartmentCertificationTypes), PendingVerification when the type requires it.</summary>
		public int? IssuesCertificationTypeId { get; set; }

		/// <summary><see cref="LmsMajorChangePolicies"/> chosen when this version was published as a major version.</summary>
		public int MajorChangePolicy { get; set; }

		/// <summary>The due date, in days from publishing, of re-assigned learners (<see cref="LmsMajorChangePolicies.Reassign"/>).</summary>
		public int? ReassignDueDays { get; set; }

		/// <summary>System content: the Academy manifest version this version was seeded from.</summary>
		public int ManifestVersion { get; set; }

		public DateTime? EffectiveOn { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
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

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public string Label => Major <= 0 ? null : Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseVersionId; }
			set { LmsCourseVersionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseVersions";

		[NotMapped]
		public string IdName => "LmsCourseVersionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Label" };
	}

	/// <summary>One approval step of one submission of a course version, with its L2 signature bound to the version hash (P1's shape).</summary>
	[Table("LmsCourseVersionApprovals")]
	public class LmsCourseVersionApproval : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseVersionApprovalId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int Round { get; set; }

		public int StepOrder { get; set; }

		public string ApproverUserId { get; set; }

		/// <summary><see cref="ProtocolApprovalDecisions"/> (shared with protocols).</summary>
		public int Decision { get; set; }

		public string DecidedByUserId { get; set; }

		public string Comment { get; set; }

		public DateTime? DecidedOn { get; set; }

		public int? SignatureRecordId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseVersionApprovalId; }
			set { LmsCourseVersionApprovalId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseVersionApprovals";

		[NotMapped]
		public string IdName => "LmsCourseVersionApprovalId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Who sees a course in the catalog (the shared <see cref="ContentAudienceKinds"/>). No rows: the whole department.</summary>
	[Table("LmsCourseAudiences")]
	public class LmsCourseAudience : IEntity, IContentAudienceRule
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseAudienceId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		public int Kind { get; set; }

		public string RefId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseAudienceId; }
			set { LmsCourseAudienceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseAudiences";

		[NotMapped]
		public string IdName => "LmsCourseAudienceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-course owners and their rights (Vector parity, §13a "activity privacy"): edit, assign, report.</summary>
	[Table("LmsCourseOwners")]
	public class LmsCourseOwner : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseOwnerId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		public string UserId { get; set; }

		/// <summary>Edit drafts, lessons and settings, and submit versions.</summary>
		public bool CanEdit { get; set; }

		/// <summary>Assign the course and manage its enrollments.</summary>
		public bool CanAssign { get; set; }

		/// <summary>See completions, pass/fail and survey results (never per-question answers, R-T12).</summary>
		public bool CanReport { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseOwnerId; }
			set { LmsCourseOwnerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseOwners";

		[NotMapped]
		public string IdName => "LmsCourseOwnerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An ordered module of a course version.</summary>
	[Table("LmsCourseModules")]
	public class LmsCourseModule : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCourseModuleId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public string Title { get; set; }

		public int SortOrder { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCourseModuleId; }
			set { LmsCourseModuleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCourseModules";

		[NotMapped]
		public string IdName => "LmsCourseModuleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An ordered lesson of a module (§12.3): blocks, video, document, an embedded protocol version, an assessment or a survey.
	/// Assessment settings (pass mark, attempts, cooldown, time limit, answer review, pre-test) live on the assessment lesson.
	/// </summary>
	[Table("LmsLessons")]
	public class LmsLesson : IEntity
	{
		public const int TitleMaxLength = 250;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLessonId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int LmsCourseModuleId { get; set; }

		/// <summary><see cref="LmsLessonKinds"/>.</summary>
		public int Kind { get; set; }

		[MaxLength(TitleMaxLength)]
		public string Title { get; set; }

		public int SortOrder { get; set; }

		/// <summary>A required lesson must be complete for the course to complete (all are, by default).</summary>
		public bool IsRequired { get; set; } = true;

		/// <summary>Blocks: the content core's ContentJson (one sanitized html block until P3).</summary>
		public string ContentJson { get; set; }

		/// <summary>Blocks: the sanitized html (StringHelpers.SanitizeHtmlForStorage); rendered through the sanitized-html path.</summary>
		public string ContentHtml { get; set; }

		/// <summary><see cref="LmsVideoSources"/>.</summary>
		public int VideoSource { get; set; }

		/// <summary>Embed: the normalized embed URL; Academy: the Academy media URL.</summary>
		public string VideoUrl { get; set; }

		/// <summary>Hosted: the media asset (LmsMediaAssets).</summary>
		public int? LmsMediaAssetId { get; set; }

		/// <summary>Caption tracks as JSON (<see cref="LmsCaptionTrack"/> list): uploaded WebVTT in storage, or an Academy track URL.</summary>
		public string CaptionTracksJson { get; set; }

		/// <summary>A text transcript shown beside the video (R-C8).</summary>
		public string TranscriptText { get; set; }

		public string DocumentUrl { get; set; }

		public string DocumentName { get; set; }

		/// <summary>ProtocolVersion lesson: the published protocol version shown (baseline content core table).</summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary>Minimum seat time on this lesson before it can be completed (Vector parity); 0 for none.</summary>
		public int MinimumSeatSeconds { get; set; }

		/// <summary>Video: how much of it must be watched (percent) before the lesson completes; 0 for none.</summary>
		public int RequiredWatchPercent { get; set; }

		/// <summary>R-C9: Omni may cache one rendition of this hosted video per device, within the cap and expiry below.</summary>
		public bool AvailableOffline { get; set; }

		public long? OfflineMaxBytes { get; set; }

		public int? OfflineExpiryDays { get; set; }

		/// <summary>Blocks: a free-response task the learner answers to complete the lesson (Vector parity).</summary>
		public string FreeResponsePrompt { get; set; }

		/// <summary>Blocks: a file the learner uploads to complete the lesson (Vector "Request a File").</summary>
		public string FileRequestPrompt { get; set; }

		// ---- Assessment ----------------------------------------------------------------------------------------------------

		public int? LmsQuestionBankId { get; set; }

		/// <summary>Questions drawn per attempt; 0 or more than the pool takes the whole pool.</summary>
		public int DrawCount { get; set; }

		/// <summary>Only questions carrying one of these tags (comma separated) are drawn; empty for any.</summary>
		public string DrawTags { get; set; }

		/// <summary>Percent needed to pass (0–100).</summary>
		public int PassMark { get; set; } = 80;

		/// <summary>Attempts allowed; 0 for unlimited.</summary>
		public int AttemptLimit { get; set; }

		public int RetakeCooldownHours { get; set; }

		/// <summary>Minutes allowed per attempt; 0 for untimed.</summary>
		public int TimeLimitMinutes { get; set; }

		/// <summary><see cref="LmsShowAnswersPolicies"/>.</summary>
		public int ShowAnswersPolicy { get; set; } = (int)LmsShowAnswersPolicies.AfterPassing;

		/// <summary>Pre-test / test-out: passing it completes the course without the other lessons (§13a).</summary>
		public bool IsPreTest { get; set; }

		public bool RandomizeAnswers { get; set; } = true;

		/// <summary>The learner accepts "I completed this myself" before starting (R-C15).</summary>
		public bool RequireAttestation { get; set; }

		/// <summary>Survey: the questions (<see cref="LmsSurveyQuestion"/> list as JSON).</summary>
		public string SurveyJson { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLessonId; }
			set { LmsLessonId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLessons";

		[NotMapped]
		public string IdName => "LmsLessonId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One caption track of a video lesson.</summary>
	public sealed class LmsCaptionTrack
	{
		/// <summary>BCP 47 language, e.g. "en".</summary>
		public string Locale { get; set; }

		public string Label { get; set; }

		/// <summary>An uploaded track: its object key in the LMS media store.</summary>
		public string ObjectKey { get; set; }

		/// <summary>An external track (Academy): its https URL.</summary>
		public string Url { get; set; }

		public static List<LmsCaptionTrack> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsCaptionTrack>();
			try
			{
				return JsonConvert.DeserializeObject<List<LmsCaptionTrack>>(json) ?? new List<LmsCaptionTrack>();
			}
			catch (JsonException)
			{
				return new List<LmsCaptionTrack>();
			}
		}
	}

	/// <summary>One question of a survey lesson.</summary>
	public sealed class LmsSurveyQuestion
	{
		public string Key { get; set; }

		/// <summary><see cref="LmsSurveyQuestionKinds"/>.</summary>
		public int Kind { get; set; }

		public string Text { get; set; }

		public List<string> Options { get; set; } = new List<string>();

		public static List<LmsSurveyQuestion> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsSurveyQuestion>();
			try
			{
				return JsonConvert.DeserializeObject<List<LmsSurveyQuestion>>(json) ?? new List<LmsSurveyQuestion>();
			}
			catch (JsonException)
			{
				return new List<LmsSurveyQuestion>();
			}
		}
	}
}
