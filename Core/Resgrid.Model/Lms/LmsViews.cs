using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// Who is acting in the LMS and what they may do, read once per request from the department's permission rows (never token
	/// claims), like the protocol reader: Manage Courses (132), Grade Assessments (133), Assign Training (126, for the free Academy)
	/// and department admin. The gate (<see cref="Gate"/>) adds the add-on answer.
	/// </summary>
	public sealed class LmsActor
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public bool IsCurrentMember { get; set; }
		public bool IsDepartmentAdmin { get; set; }

		/// <summary>Holds Manage Courses (132): author and publish every department course and bank.</summary>
		public bool CanManageCourses { get; set; }

		/// <summary>Holds Grade Assessments (133): the grading queue, attempt answers and item analysis.</summary>
		public bool CanGrade { get; set; }

		/// <summary>Holds Assign Training (126): assigns the free Academy lessons and paths.</summary>
		public bool CanAssignTraining { get; set; }

		public LmsDepartmentGate Gate { get; set; }

		public ContentAudienceMemberFacts Facts { get; set; }

		public bool MayManage => IsCurrentMember && (IsDepartmentAdmin || CanManageCourses);

		public bool MayGrade => IsCurrentMember && (IsDepartmentAdmin || CanGrade);

		public bool MayAssignAcademy => IsCurrentMember && (IsDepartmentAdmin || CanAssignTraining || CanManageCourses);

		/// <summary>The person's rights on one course: managers hold all; owners what their row says.</summary>
		public LmsCourseRights RightsOn(LmsCourse course, IEnumerable<LmsCourseOwner> owners)
		{
			if (course == null || !IsCurrentMember || course.IsSystem || course.DepartmentId != DepartmentId)
				return new LmsCourseRights();
			if (MayManage)
				return new LmsCourseRights { Edit = true, Assign = true, Report = true, Publish = true };
			var row = (owners ?? Enumerable.Empty<LmsCourseOwner>()).FirstOrDefault(o => o != null && o.LmsCourseId == course.LmsCourseId && string.Equals(o.UserId, UserId, StringComparison.OrdinalIgnoreCase));
			return row == null ? new LmsCourseRights() : new LmsCourseRights { Edit = row.CanEdit, Assign = row.CanAssign, Report = row.CanReport };
		}

		public static LmsActor Nobody(int departmentId) => new LmsActor { DepartmentId = departmentId, Gate = new LmsDepartmentGate { DepartmentId = departmentId } };
	}

	/// <summary>A person's rights on one course (per-course owners, §13a). Publishing needs Manage Courses.</summary>
	public sealed class LmsCourseRights
	{
		public bool Edit { get; set; }
		public bool Assign { get; set; }
		public bool Report { get; set; }
		public bool Publish { get; set; }
		public bool Any => Edit || Assign || Report || Publish;
	}

	/// <summary>The outcome of an LMS action: what changed, or why it was refused.</summary>
	public sealed class LmsResult
	{
		public LmsErrors Error { get; set; }

		/// <summary>A refusal that came from the shared content-core lifecycle (submit, approve, publish).</summary>
		public ProtocolLifecycleErrors LifecycleError { get; set; }

		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public LmsEnrollment Enrollment { get; set; }
		public SignatureRecord Signature { get; set; }
		public int Count { get; set; }
		public object Value { get; set; }

		public bool Succeeded => Error == LmsErrors.None && LifecycleError == ProtocolLifecycleErrors.None;

		public static LmsResult Fail(LmsErrors error) => new LmsResult { Error = error };

		public static LmsResult Fail(ProtocolLifecycleErrors error) => new LmsResult { LifecycleError = error };
	}

	/// <summary>A new course or its catalog fields.</summary>
	public sealed class LmsCourseInput
	{
		public string Title { get; set; }
		public string Code { get; set; }
		public string Summary { get; set; }
		public string Tags { get; set; }
		public bool IsSelfEnrollable { get; set; }
		public List<ContentAudienceRule> Audience { get; set; }
	}

	/// <summary>A draft version's settings and overview.</summary>
	public sealed class LmsVersionSettingsInput
	{
		public string Title { get; set; }
		public string OverviewHtml { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public int? CertificateTemplateId { get; set; }
		public int? IssuesCertificationTypeId { get; set; }
		public long ExpectedRowVersion { get; set; }
	}

	/// <summary>A lesson as the builder saves it (new when <see cref="LmsLessonId"/> is 0).</summary>
	public sealed class LmsLessonInput
	{
		public int LmsLessonId { get; set; }
		public int LmsCourseVersionId { get; set; }
		public int LmsCourseModuleId { get; set; }
		public int Kind { get; set; }
		public string Title { get; set; }
		public bool IsRequired { get; set; } = true;
		public string ContentHtml { get; set; }
		public int VideoSource { get; set; }
		public string VideoUrl { get; set; }
		public int? LmsMediaAssetId { get; set; }
		public string TranscriptText { get; set; }
		public string DocumentUrl { get; set; }
		public string DocumentName { get; set; }
		public int? ProtocolVersionId { get; set; }
		public int MinimumSeatSeconds { get; set; }
		public int RequiredWatchPercent { get; set; }
		public bool AvailableOffline { get; set; }
		public long? OfflineMaxBytes { get; set; }
		public int? OfflineExpiryDays { get; set; }
		public string FreeResponsePrompt { get; set; }
		public string FileRequestPrompt { get; set; }
		public int? LmsQuestionBankId { get; set; }
		public int DrawCount { get; set; }
		public string DrawTags { get; set; }
		public int PassMark { get; set; } = 80;
		public int AttemptLimit { get; set; }
		public int RetakeCooldownHours { get; set; }
		public int TimeLimitMinutes { get; set; }
		public int ShowAnswersPolicy { get; set; } = (int)LmsShowAnswersPolicies.AfterPassing;
		public bool IsPreTest { get; set; }
		public bool RandomizeAnswers { get; set; } = true;
		public bool RequireAttestation { get; set; }
		public List<LmsSurveyQuestion> SurveyQuestions { get; set; }
	}

	/// <summary>A course version's structure: modules with their lessons in order.</summary>
	public sealed class LmsCourseOutline
	{
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public List<LmsOutlineModule> Modules { get; set; } = new List<LmsOutlineModule>();

		public IEnumerable<LmsLesson> Lessons => Modules.SelectMany(m => m.Lessons);
	}

	public sealed class LmsOutlineModule
	{
		public LmsCourseModule Module { get; set; }
		public List<LmsLesson> Lessons { get; set; } = new List<LmsLesson>();
	}

	/// <summary>A row of the officer course list.</summary>
	public sealed class LmsCourseRow
	{
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Current { get; set; }
		public LmsCourseVersion OpenDraft { get; set; }
		public int Enrolled { get; set; }
		public int Completed { get; set; }
		public LmsCourseRights Rights { get; set; }
	}

	/// <summary>A version waiting for an approver.</summary>
	public sealed class LmsReviewItem
	{
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public LmsCourseVersionApproval Step { get; set; }
		public bool CanDecide { get; set; }
	}

	/// <summary>One of the member's courses (My courses, beside My Training).</summary>
	public sealed class LmsMyCourse
	{
		public LmsEnrollment Enrollment { get; set; }
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public bool CanRun { get; set; }
		public bool IsOverdue { get; set; }
		public LmsErrors BlockedBy { get; set; }
	}

	/// <summary>A catalog entry the member may see and enrol in.</summary>
	public sealed class LmsCatalogEntry
	{
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public LmsEnrollment OpenEnrollment { get; set; }
		public bool CanEnroll { get; set; }
	}

	/// <summary>What the player page renders (R-C8): the outline of the version the learner takes, progress, and the lesson open.</summary>
	public sealed class LmsPlayerView
	{
		public LmsErrors Error { get; set; }
		public LmsEnrollment Enrollment { get; set; }
		public LmsCourseOutline Outline { get; set; }
		public Dictionary<int, LmsLessonProgress> Progress { get; set; } = new Dictionary<int, LmsLessonProgress>();
		public LmsLesson Lesson { get; set; }
		public LmsLessonProgress LessonProgress { get; set; }

		/// <summary>Lessons that cannot be opened yet (an earlier required one is not complete).</summary>
		public HashSet<int> LockedLessonIds { get; set; } = new HashSet<int>();

		public int? PreviousLessonId { get; set; }
		public int? NextLessonId { get; set; }

		/// <summary>Assessment lesson: the learner's attempts (newest first) and whether another may start now.</summary>
		public List<LmsAssessmentAttempt> Attempts { get; set; } = new List<LmsAssessmentAttempt>();
		public LmsErrors AttemptBlockedBy { get; set; }
		public DateTime? NextAttemptOn { get; set; }

		/// <summary>ProtocolVersion lesson: the protocol version shown (its published html).</summary>
		public ProtocolVersion Protocol { get; set; }

		/// <summary>Video lesson: whether the hosted media may stream now (lapse rule 6).</summary>
		public bool HostedVideoPlayable { get; set; }

		public LmsMediaAsset Media { get; set; }

		public List<LmsCaptionTrack> Captions { get; set; } = new List<LmsCaptionTrack>();

		public bool SurveyAnswered { get; set; }
	}

	/// <summary>A progress heartbeat or lesson change from the player.</summary>
	public sealed class LmsProgressInput
	{
		/// <summary>Seconds on the lesson since the previous heartbeat (visible page only); capped by the server.</summary>
		public int Seconds { get; set; }
		public int? VideoPositionSeconds { get; set; }
		public int? WatchedPercent { get; set; }
	}

	/// <summary>What completing a lesson carries: the free response, the requested file.</summary>
	public sealed class LmsLessonCompletionInput
	{
		public string ResponseText { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public byte[] FileData { get; set; }
	}

	/// <summary>An attempt as the learner takes it: questions and options only (R-T11).</summary>
	public sealed class LmsAttemptView
	{
		public LmsErrors Error { get; set; }
		public LmsAssessmentAttempt Attempt { get; set; }
		public LmsLesson Lesson { get; set; }
		public List<LmsLearnerQuestion> Questions { get; set; } = new List<LmsLearnerQuestion>();
		public int? SecondsRemaining { get; set; }
	}

	/// <summary>A finished attempt as the learner may see it under the lesson's answer-review policy.</summary>
	public sealed class LmsAttemptResultView
	{
		public LmsErrors Error { get; set; }
		public LmsAssessmentAttempt Attempt { get; set; }
		public LmsLesson Lesson { get; set; }
		public bool ShowCorrectness { get; set; }
		public bool ShowAnswers { get; set; }

		/// <summary>Per question; correct keys and explanations only when <see cref="ShowAnswers"/>.</summary>
		public List<LmsReviewedQuestion> Questions { get; set; } = new List<LmsReviewedQuestion>();

		/// <summary>The course completed with this attempt (a pass on the last requirement, or a passed pre-test).</summary>
		public bool CourseCompleted { get; set; }
	}

	public sealed class LmsReviewedQuestion
	{
		public LmsLearnerQuestion Question { get; set; }
		public LmsAttemptAnswer Answer { get; set; }
		public bool? Correct { get; set; }
		public bool AwaitingGrade { get; set; }
		public List<string> CorrectKeys { get; set; }
		public string Explanation { get; set; }
	}

	/// <summary>A short answer waiting in the grading queue (permission 133).</summary>
	public sealed class LmsGradingItem
	{
		public LmsAssessmentAttempt Attempt { get; set; }
		public string CourseTitle { get; set; }
		public string LessonTitle { get; set; }
		public List<LmsGradingQuestion> Questions { get; set; } = new List<LmsGradingQuestion>();
	}

	public sealed class LmsGradingQuestion
	{
		public int QuestionId { get; set; }
		public string Text { get; set; }
		public string ModelAnswer { get; set; }
		public string AnswerText { get; set; }
		public decimal Points { get; set; }
	}

	/// <summary>Item analysis of one assessment lesson.</summary>
	public sealed class LmsItemAnalysisReport
	{
		public LmsCourse Course { get; set; }
		public LmsLesson Lesson { get; set; }
		public int Attempts { get; set; }
		public decimal PassRate { get; set; }
		public List<LmsItemStatistic> Items { get; set; } = new List<LmsItemStatistic>();
	}

	/// <summary>A printable test: questions in a fixed draw, with the key only for the author's copy.</summary>
	public sealed class LmsPrintableTest
	{
		public LmsCourse Course { get; set; }
		public LmsLesson Lesson { get; set; }
		public List<LmsSnapshotQuestion> Questions { get; set; } = new List<LmsSnapshotQuestion>();
		public bool IncludesKey { get; set; }
	}

	/// <summary>Anonymous aggregate results of a survey lesson (shown from <see cref="MinimumResponses"/> responses).</summary>
	public sealed class LmsSurveyReport
	{
		public const int MinimumResponses = 3;

		public LmsCourse Course { get; set; }
		public LmsLesson Lesson { get; set; }
		public int Responses { get; set; }
		public bool Hidden => Responses < MinimumResponses;
		public List<LmsSurveyQuestionReport> Questions { get; set; } = new List<LmsSurveyQuestionReport>();
	}

	public sealed class LmsSurveyQuestionReport
	{
		public LmsSurveyQuestion Question { get; set; }
		public decimal? Average { get; set; }
		public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
		public List<string> Comments { get; set; } = new List<string>();
	}

	/// <summary>A row of a course's enrollment list (officer view; pass/fail only, never answers, R-T12).</summary>
	public sealed class LmsEnrollmentRow
	{
		public LmsEnrollment Enrollment { get; set; }
		public string VersionLabel { get; set; }
		public bool IsOverdue { get; set; }
	}

	/// <summary>Who an assignment reached and who it skipped.</summary>
	public sealed class LmsAssignOutcome
	{
		public LmsErrors Error { get; set; }
		public int Created { get; set; }
		public int AlreadyEnrolled { get; set; }
		public List<ContentAudienceSkip> Skipped { get; set; } = new List<ContentAudienceSkip>();
	}

	/// <summary>Hosted media storage per department against the included 100 GB (report only; no overage billing).</summary>
	public sealed class LmsStorageReport
	{
		public long IncludedBytes { get; set; }
		public long UsedBytes { get; set; }
		public int Assets { get; set; }
		public List<LmsMediaAsset> Largest { get; set; } = new List<LmsMediaAsset>();
		public decimal PercentUsed => IncludedBytes <= 0 ? 0 : Math.Round(UsedBytes * 100m / IncludedBytes, 1);
		public bool OverIncluded => UsedBytes > IncludedBytes;
	}

	/// <summary>One video Omni may cache offline (R-C9): one rendition, its size cap and expiry, a signed URL.</summary>
	public sealed class LmsOfflineVideo
	{
		public int LmsEnrollmentId { get; set; }
		public int LmsLessonId { get; set; }
		public string Title { get; set; }
		public string Url { get; set; }
		public long Bytes { get; set; }
		public long MaxBytes { get; set; }
		public DateTime UrlExpiresOn { get; set; }
		public DateTime CacheExpiresOn { get; set; }
	}

	/// <summary>A lesson of the Academy page: the course, whether it is visible to the department, and the member's enrollment.</summary>
	public sealed class AcademyLessonView
	{
		public AcademyCourseDefinition Definition { get; set; }
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public LmsEnrollment MyEnrollment { get; set; }
		public bool Completed { get; set; }
	}

	public sealed class AcademyPathView
	{
		public AcademyRolePath Path { get; set; }
		public List<AcademyLessonView> Lessons { get; set; } = new List<AcademyLessonView>();
		public int AssignedMembers { get; set; }
		public bool AssignedToMe { get; set; }
		public int CompletedByMe => Lessons.Count(l => l.Completed);
	}

	public sealed class AcademyCatalogView
	{
		public LmsErrors Error { get; set; }
		public List<AcademyLessonView> Lessons { get; set; } = new List<AcademyLessonView>();
		public List<AcademyPathView> Paths { get; set; } = new List<AcademyPathView>();
		public bool CanAssign { get; set; }
	}

	/// <summary>Per-member progress on an assigned role path.</summary>
	public sealed class AcademyPathProgressRow
	{
		public string UserId { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public int Completed { get; set; }
		public int Total { get; set; }
		public Dictionary<string, bool> Lessons { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
	}
}
