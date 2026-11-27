using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T2 LMS and A1 Academy (protocols-training-knowledge-plan §12.3, §13 T2, §15 A1): the member runtime Omni would use. Quiz
	// payloads carry questions and options only; the answer key never leaves the server (R-T11). All times are UTC. Omni itself is
	// out of scope for this build.

	/// <summary>One of the member's LMS courses (Academy lessons included).</summary>
	public class LmsMyCourseData
	{
		public int EnrollmentId { get; set; }
		public int CourseId { get; set; }
		public int CourseVersionId { get; set; }
		public string Title { get; set; }
		/// <summary>The version the member takes (R-L12: they finish the version they started).</summary>
		public string VersionLabel { get; set; }
		public bool IsAcademy { get; set; }
		/// <summary>LmsEnrollmentStatuses name: NotStarted, InProgress, Completed.</summary>
		public string Status { get; set; }
		public int ProgressPercent { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public bool IsOverdue { get; set; }
		/// <summary>False when the department's LMS lapsed past its window (or is dark): completed history stays readable.</summary>
		public bool CanRun { get; set; }
		public decimal Hours { get; set; }
	}

	public class LmsMyCoursesResult : StandardApiResponseV4Base
	{
		public List<LmsMyCourseData> Data { get; set; } = new List<LmsMyCourseData>();
	}

	/// <summary>A lesson as the player shows it (content is sanitized HTML; video sources carry a URL the client may play).</summary>
	public class LmsLessonData
	{
		public int LessonId { get; set; }
		public int ModuleId { get; set; }
		public string ModuleTitle { get; set; }
		public string Title { get; set; }
		/// <summary>LmsLessonKinds name: Blocks, Video, Document, ProtocolVersion, Assessment, Survey.</summary>
		public string Kind { get; set; }
		public bool IsRequired { get; set; }
		public bool IsLocked { get; set; }
		/// <summary>LmsLessonProgressStatuses name.</summary>
		public string Progress { get; set; }
		public int SeatSeconds { get; set; }
		public int MinimumSeatSeconds { get; set; }
		public int VideoPositionSeconds { get; set; }
		public int WatchedPercent { get; set; }
		public int RequiredWatchPercent { get; set; }
		public string ContentHtml { get; set; }
		/// <summary>LmsVideoSources name: Hosted, Embed, Academy.</summary>
		public string VideoSource { get; set; }
		/// <summary>Embed and Academy videos: the URL; hosted video streams from the web player's signed playlist.</summary>
		public string VideoUrl { get; set; }
		public string TranscriptText { get; set; }
		public string DocumentUrl { get; set; }
		public string FreeResponsePrompt { get; set; }
		public string FileRequestPrompt { get; set; }
		/// <summary>R-T11: only an untimed, unlimited, non-pre-test quiz that issues no certification may be answered offline.</summary>
		public bool MayAnswerOffline { get; set; }
		public bool AvailableOffline { get; set; }

		/// <summary>
		/// Round 10: a ProtocolVersion lesson's protocol version (open it with ProtocolContent/GetVersionContent, which applies the protocol read
		/// rule); null for every other kind.
		/// </summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary>Round 10: the files the member uploaded for this lesson (a file-request lesson's answer); the bytes come from Lms/GetLessonFile.</summary>
		public List<LmsLessonFileData> Files { get; set; } = new List<LmsLessonFileData>();

		/// <summary>Round 10: a Survey lesson's questions (answered with Lms/SubmitSurvey); empty for every other kind.</summary>
		public List<LmsSurveyQuestionData> SurveyQuestions { get; set; } = new List<LmsSurveyQuestionData>();

		/// <summary>
		/// Round 10: a Video lesson's caption tracks. An uploaded track has no Url here: read it with Lms/GetLessonCaptions (bearer) or the signed
		/// URL Lms/GetLessonPlaylist hands out; an Academy track carries its public https URL.
		/// </summary>
		public List<LmsCaptionTrackData> Captions { get; set; } = new List<LmsCaptionTrackData>();
	}

	public class LmsCourseResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Null when the course may run now; otherwise the LmsErrors name of why not (Status is Failure then): "NotLicensed" when the member
		/// holds no LMS seat, "LapseWindowEnded" when the add-on lapsed and the window to finish has passed, "AddonRequired" and so on. Round 10
		/// A7: the enrollment is read-only then, so the lessons carry their titles, kinds and the member's progress only (no content, media
		/// links, prompts, survey questions or captions).
		/// </summary>
		public string Error { get; set; }

		public LmsMyCourseData Course { get; set; }
		public int? CurrentLessonId { get; set; }
		public List<LmsLessonData> Lessons { get; set; } = new List<LmsLessonData>();
	}

	/// <summary>A progress heartbeat or lesson change (seconds are capped by the server's clock).</summary>
	public class LmsProgressInput
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		public int Seconds { get; set; }
		public int? VideoPositionSeconds { get; set; }
		public int? WatchedPercent { get; set; }
	}

	public class LmsCompleteLessonInput
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		public string ResponseText { get; set; }

		/// <summary>
		/// Round 10: a file-request lesson's answer (optional otherwise): the file name, its type (PDF, JPEG, PNG, GIF, WEBP, plain text, DOC,
		/// DOCX, XLS or XLSX) and its bytes as base64, at most 10 MB. Refusals: FileTooLarge, UnsupportedFile, ResponseRequired.
		/// </summary>
		public string FileName { get; set; }
		public string FileContentType { get; set; }
		public string FileData { get; set; }
	}

	/// <summary>The outcome of an LMS action: Status is Success or Failure; Error names the LmsErrors value.</summary>
	public class LmsActionResult : StandardApiResponseV4Base
	{
		public string Error { get; set; }
		public bool CourseCompleted { get; set; }
		public int ProgressPercent { get; set; }
		/// <summary>The enrollment the action concerned (Enroll returns the new or existing one).</summary>
		public int? EnrollmentId { get; set; }
	}

	public class LmsStartAttemptInput
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		public bool Attested { get; set; }
	}

	public class LmsQuestionOptionData
	{
		public string Key { get; set; }
		public string Text { get; set; }
	}

	/// <summary>A question without its key.</summary>
	public class LmsQuestionData
	{
		public int QuestionId { get; set; }
		/// <summary>LmsQuestionKinds name: SingleChoice, MultiChoice, TrueFalse, Ordering, ShortAnswer.</summary>
		public string Kind { get; set; }
		public string Text { get; set; }
		public string ImageUrl { get; set; }
		public decimal Points { get; set; }
		public List<LmsQuestionOptionData> Options { get; set; } = new List<LmsQuestionOptionData>();
	}

	public class LmsAttemptResult : StandardApiResponseV4Base
	{
		public string Error { get; set; }
		public int AttemptId { get; set; }
		public DateTime? DeadlineOn { get; set; }
		public int? SecondsRemaining { get; set; }
		public List<LmsQuestionData> Questions { get; set; } = new List<LmsQuestionData>();
	}

	public class LmsAnswerInput
	{
		public int QuestionId { get; set; }
		/// <summary>Chosen option keys (choice) or keys in order (ordering).</summary>
		public List<string> Keys { get; set; } = new List<string>();
		/// <summary>A short answer.</summary>
		public string Text { get; set; }
	}

	public class LmsSubmitAttemptInput
	{
		public int AttemptId { get; set; }
		public List<LmsAnswerInput> Answers { get; set; } = new List<LmsAnswerInput>();
	}

	public class LmsReviewedQuestionData
	{
		public int QuestionId { get; set; }
		public bool? Correct { get; set; }
		public bool AwaitingGrade { get; set; }
		/// <summary>Only when the lesson's answer-review policy allows it.</summary>
		public List<string> CorrectKeys { get; set; }
		public string Explanation { get; set; }
	}

	public class LmsAttemptOutcomeResult : StandardApiResponseV4Base
	{
		public string Error { get; set; }
		public int AttemptId { get; set; }
		public decimal Percent { get; set; }
		/// <summary>Null while short answers wait for a grader.</summary>
		public bool? Passed { get; set; }
		public bool CourseCompleted { get; set; }
		public List<LmsReviewedQuestionData> Questions { get; set; } = new List<LmsReviewedQuestionData>();
	}

	/// <summary>One hosted video Omni may cache offline (R-C9): one rendition, its cap and the cache expiry.</summary>
	public class LmsOfflineVideoData
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		public string Title { get; set; }
		public string Url { get; set; }
		public long Bytes { get; set; }
		public long MaxBytes { get; set; }
		public DateTime UrlExpiresOn { get; set; }
		public DateTime CacheExpiresOn { get; set; }
	}

	public class LmsOfflineManifestResult : StandardApiResponseV4Base
	{
		public List<LmsOfflineVideoData> Data { get; set; } = new List<LmsOfflineVideoData>();
	}

	public class LmsCrewScanInput
	{
		public int ViewingId { get; set; }
		public string Code { get; set; }
	}

	/// <summary>An Academy lesson the department may take (visibility follows entitlement).</summary>
	public class LmsAcademyLessonData
	{
		public string LessonId { get; set; }
		public int CourseId { get; set; }
		public string Title { get; set; }
		public double Minutes { get; set; }
		public decimal Hours { get; set; }
		public bool IsWalkthrough { get; set; }
		public int? EnrollmentId { get; set; }
		public bool Completed { get; set; }
	}

	public class LmsAcademyResult : StandardApiResponseV4Base
	{
		public List<LmsAcademyLessonData> Data { get; set; } = new List<LmsAcademyLessonData>();
	}
}
