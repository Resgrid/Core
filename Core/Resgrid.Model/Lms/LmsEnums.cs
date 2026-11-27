namespace Resgrid.Model.Lms
{
	/// <summary>
	/// Who published a course (LmsCourses.PublisherKind). Persisted: append only. System content (the Resgrid Academy, A1)
	/// carries DepartmentId 0: no department owns it, every department runs it (R-S2) and no department cleanup touches it.
	/// </summary>
	public enum LmsPublisherKinds
	{
		/// <summary>The department's own course: authoring and new assignments need the LMS add-on (§17.3).</summary>
		Department = 0,

		/// <summary>Resgrid system-published (Academy): runs for every department with no seat, never counts against limits.</summary>
		System = 1
	}

	/// <summary>A lesson's kind (LmsLessons.Kind, plan §12.3). Persisted: append only. Scorm, Cmi5 and LiveSession are reserved (T4/T5).</summary>
	public enum LmsLessonKinds
	{
		/// <summary>Rich text (the content core's sanitized html block), optionally with a free-response or file-request task.</summary>
		Blocks = 0,

		/// <summary>A hosted video (HLS), a YouTube or Vimeo link, or an Academy video.</summary>
		Video = 1,

		/// <summary>A document link or attachment to read.</summary>
		Document = 2,

		/// <summary>A published protocol version, embedded as it was published.</summary>
		ProtocolVersion = 3,

		/// <summary>T5: a SCORM 1.2 or 2004 package (LmsLessons.LmsContentPackageId) played in the sandboxed runtime player.</summary>
		Scorm = 4,

		/// <summary>T5: a cmi5 package (LmsLessons.LmsContentPackageId); its AUs report to the minimal LRS of the launch.</summary>
		Cmi5 = 5,

		/// <summary>An assessment drawn from a question bank; scored on the server.</summary>
		Assessment = 6,

		/// <summary>A course evaluation survey: anonymous, aggregate results only.</summary>
		Survey = 7,

		/// <summary>Reserved (T4): a live class session. Not built.</summary>
		LiveSession = 8,

		/// <summary>T5: an LTI 1.3 tool launch (LmsLessons.LmsLtiToolId), completed by the score the tool passes back (AGS).</summary>
		LtiLink = 9
	}

	/// <summary>Where a video lesson's media comes from (LmsLessons.VideoSource). Persisted: append only.</summary>
	public enum LmsVideoSources
	{
		None = 0,

		/// <summary>Uploaded to the department's object storage and transcoded to HLS by worker 87 (LMS, decision 9).</summary>
		Hosted = 1,

		/// <summary>A YouTube or Vimeo link (EmbedAllowList), free with the baseline too.</summary>
		Embed = 2,

		/// <summary>A Resgrid Academy video (academy.resgrid.com media with its caption track), A1 only.</summary>
		Academy = 3
	}

	/// <summary>Question types (LmsQuestions.Kind, plan §12.3). Persisted: append only.</summary>
	public enum LmsQuestionKinds
	{
		SingleChoice = 0,
		MultiChoice = 1,
		TrueFalse = 2,

		/// <summary>Put the options in order; the options as stored are the correct order.</summary>
		Ordering = 3,

		/// <summary>A typed answer graded by hand in the grading queue (permission 133).</summary>
		ShortAnswer = 4
	}

	/// <summary>When a learner sees the correct answers after an attempt (LmsLessons.ShowAnswersPolicy). Persisted.</summary>
	public enum LmsShowAnswersPolicies
	{
		/// <summary>Score and pass/fail only.</summary>
		Never = 0,

		/// <summary>Which questions were right or wrong, without the correct answers.</summary>
		CorrectnessOnly = 1,

		/// <summary>The correct answers and explanations, only once the learner passed.</summary>
		AfterPassing = 2,

		/// <summary>The correct answers and explanations after every submitted attempt.</summary>
		Always = 3
	}

	/// <summary>An assessment attempt's state (LmsAssessmentAttempts.Status). Persisted: append only.</summary>
	public enum LmsAttemptStatuses
	{
		InProgress = 0,

		/// <summary>Scored automatically; Passed is set.</summary>
		Scored = 1,

		/// <summary>Has short answers waiting in the grading queue; Passed is null until graded.</summary>
		PendingGrading = 2,

		/// <summary>The grading queue finished it; Passed is set.</summary>
		Graded = 3,

		/// <summary>The time limit ran out before it was submitted.</summary>
		Expired = 4
	}

	/// <summary>How a member came to a course (LmsEnrollments.Source). Persisted: append only.</summary>
	public enum LmsEnrollmentSources
	{
		SelfEnrolled = 0,
		Assigned = 1,

		/// <summary>An Academy role path (A1) assigned to the member.</summary>
		RolePath = 2,

		/// <summary>A major version re-assigned completed learners (R-L12).</summary>
		Reassigned = 3,

		/// <summary>The member scanned a crew-viewing QR (R-C4).</summary>
		CrewViewing = 4,

		/// <summary>Round 7B: an instructor recorded the course completed for the member (permission 135).</summary>
		RecordedCompletion = 5
	}

	/// <summary>An enrollment's state (LmsEnrollments.Status). Persisted: append only.</summary>
	public enum LmsEnrollmentStatuses
	{
		NotStarted = 0,
		InProgress = 1,
		Completed = 2,

		/// <summary>Removed by an officer, or replaced; kept for the history.</summary>
		Withdrawn = 3
	}

	/// <summary>A lesson's progress state (LmsLessonProgress.Status).</summary>
	public enum LmsLessonProgressStatuses
	{
		NotStarted = 0,
		InProgress = 1,
		Completed = 2
	}

	/// <summary>What happens to completed learners when a major version is published (R-L12). Persisted.</summary>
	public enum LmsMajorChangePolicies
	{
		/// <summary>Completions stand; nobody is told.</summary>
		Nothing = 0,

		/// <summary>Completed learners are told a new major version exists.</summary>
		Notify = 1,

		/// <summary>Completed learners are assigned the new version with a new due date.</summary>
		Reassign = 2
	}

	/// <summary>A hosted media object's state (LmsMediaAssets.Status, worker 87). Persisted: append only.</summary>
	public enum LmsMediaStatuses
	{
		/// <summary>Uploading: the source object is not complete yet.</summary>
		Uploading = 0,

		/// <summary>Uploaded and waiting for worker 87.</summary>
		Queued = 1,

		/// <summary>Claimed by worker 87 (ClaimedOn says when; a stale claim goes back to the queue).</summary>
		Transcoding = 2,

		/// <summary>HLS renditions are in storage and stream through signed URLs.</summary>
		Ready = 3,

		/// <summary>Transcoding failed MaxAttempts times; the author uploads again.</summary>
		Failed = 4,

		/// <summary>Lapse rule 6: 90 days after the add-on ended the media stops streaming (it stays exportable).</summary>
		Archived = 5
	}

	/// <summary>A hosted media object's kind (LmsMediaAssets.Kind).</summary>
	public enum LmsMediaKinds
	{
		Video = 0,

		/// <summary>A WebVTT caption track.</summary>
		Captions = 1
	}

	/// <summary>Survey question kinds (in LmsLessons.SurveyJson).</summary>
	public enum LmsSurveyQuestionKinds
	{
		/// <summary>1 to 5.</summary>
		Rating = 0,
		Choice = 1,
		Text = 2
	}

	/// <summary>Why the LMS refused something. Each maps to a localized message on the web.</summary>
	public enum LmsErrors
	{
		None = 0,
		NotFound,
		NotAuthorized,

		/// <summary>The department's LMS is off (flag or module) or the add-on is not active (authoring, new assignments).</summary>
		AddonRequired,

		/// <summary>The course cannot be run: the add-on lapsed and the lapse window (due date or 30 days) has passed.</summary>
		LapseWindowEnded,

		/// <summary>Hidden by entitlement (an Academy lesson for a module, flag or add-on the department does not have).</summary>
		NotEntitled,

		NotDraft,
		StaleCopy,
		InvalidInput,
		AttemptLimitReached,

		/// <summary>The retake cooldown has not passed.</summary>
		CooldownActive,

		/// <summary>The attempt's time limit ran out.</summary>
		TimeExpired,

		AlreadySubmitted,

		/// <summary>Minimum seat time or watched percent not reached yet.</summary>
		SeatTimeNotMet,

		/// <summary>An assessment lesson that is not passed yet, or a survey not answered.</summary>
		RequirementNotMet,

		/// <summary>The free-response or file-request task is missing.</summary>
		ResponseRequired,

		/// <summary>An earlier required lesson is not complete (lessons run in order).</summary>
		LessonLocked,

		/// <summary>Hosted video storage is not configured or the media is not ready.</summary>
		MediaUnavailable,

		/// <summary>A crew-viewing code that is wrong or has rotated away.</summary>
		CodeExpired,

		FileTooLarge,
		UnsupportedFile,

		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): the acting member holds no LMS seat, so they cannot act as a learner on department LMS
		/// content (the Academy, the free baseline and their own completed records stay open). The v4 API reports it as "NotLicensed".
		/// </summary>
		NotLicensed
	}
}
