using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T2 (courses and assessments) and A1 (the Academy as a system course set). LMS services consume baseline services
	// (learning records, certifications, audiences, signatures, the protocol content core); baseline services never call these
	// (§17.3). Department content needs the LMS gate; system-published content runs for every department (R-S2).

	/// <summary>The department's LMS gate for one request (§17.3), plus the lapse clock the lapse rules run from.</summary>
	public interface ILmsGateService
	{
		/// <summary>
		/// The flag, module and add-on answers (one billing call, no entitlement cache) and when the add-on was last seen active.
		/// Stamps LastEntitledOn (at most hourly) when the add-on is active. Never call from a sweep (R-L9).
		/// </summary>
		Task<LmsDepartmentGate> GetGateAsync(int departmentId);

		/// <summary>The rights of a person in the department (permission rows, not claims) with the gate.</summary>
		Task<LmsActor> GetActorAsync(int departmentId, string userId);

		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): the members who may act as learners on department LMS content
		/// (<see cref="ILmsAccessService.GetLearnerSeatHoldersAsync"/>; one billing read). A worker asks once per department per run (R-L9).
		/// </summary>
		Task<HashSet<string>> GetLearnerSeatHoldersAsync(int departmentId);

		/// <summary>
		/// Round 10 A7: whether this member holds the LMS seat a learner action on department LMS content needs (see
		/// <see cref="LmsLearnerSeats"/> for which actions ask). Lapse-aware: during a lapse a member who held a seat keeps it, and the
		/// lapse rules decide what still runs. False when the flag is off or anything cannot be read (fails closed).
		/// </summary>
		Task<bool> HoldsLearnerSeatAsync(int departmentId, string userId);
	}

	/// <summary>
	/// What P2's acknowledgment quizzes (and anything else outside the LMS) may ask: whether a member passed a course version. P2
	/// links a protocol version's acknowledgment to an LMS course version with question banks (§17.2, T2 item 7).
	/// </summary>
	public interface ILmsCourseResults
	{
		/// <summary>True when the member completed that exact course version (every required lesson, assessments passed).</summary>
		Task<bool> IsCourseVersionPassedAsync(string userId, int courseVersionId);
	}

	/// <summary>Course authoring on the shared content core (T2 item 1): versions, approval with L2 signatures, publish, owners, audience.</summary>
	/// <summary>One course set copy to write (round 10 A3).</summary>
	public sealed class LmsSetCopyRequest
	{
		public int SubscriberDepartmentId { get; set; }
		public int PublisherDepartmentId { get; set; }
		public int SourceVersionId { get; set; }
		/// <summary>The subscriber's course (updates only).</summary>
		public int LocalCourseId { get; set; }
		public string UserId { get; set; }
		public string ChangeSummary { get; set; }
		public bool Major { get; set; }
	}

	/// <summary>What a course set copy leaves for the subscriber to do (round 10 A3): LTI tools to register before their lessons open.</summary>
	public sealed class LmsSetCopyNotes
	{
		public List<string> ToolsToRegister { get; set; } = new List<string>();
	}

	/// <summary>Why a course's stored lesson files cannot travel to a subscriber now (ContentSets Problem_* keys).</summary>
	public static class LmsSetFileProblems
	{
		public const string MediaNotReady = "media_not_ready";
		public const string PackageNotReady = "package_not_ready";
		public const string ToolMissing = "lti_tool_missing";
	}

	/// <summary>The subscriber's LTI registration a copied lesson uses.</summary>
	public sealed class LmsSetTool
	{
		public int LmsLtiToolId { get; set; }
		public string Name { get; set; }
		public bool IsActive { get; set; }
		public bool AllowGradePassback { get; set; }
		/// <summary>Made by this copy (inactive until the subscriber registers it with the vendor).</summary>
		public bool Created { get; set; }
	}

	/// <summary>
	/// Hosted video, SCORM/cmi5 packages and LTI tools of a course set copy (round 10 A3): video and packages are shared as the subscriber's
	/// own rows over the publisher's stored objects; an LTI tool becomes the subscriber's own (inactive) registration with the same endpoints.
	/// </summary>
	public interface ILmsSetFiles
	{
		/// <summary>Why the lessons' stored files cannot be shared now (a video or package not ready, a removed tool), or null.</summary>
		Task<string> GetProblemAsync(int publisherDepartmentId, IEnumerable<LmsLesson> lessons);

		/// <summary>The subscriber's media asset over the publisher's ready video (reused across updates), or null.</summary>
		Task<int?> ShareVideoAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourceAssetId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The subscriber's package over the publisher's ready package (reused across updates), or null.</summary>
		Task<int?> SharePackageAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourcePackageId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The subscriber's registration for the publisher's tool (an existing one with the same endpoints, else a new inactive one), or null.</summary>
		Task<LmsSetTool> PrepareToolAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourceToolId, string userId, CancellationToken cancellationToken = default);
	}

	public interface ILmsCourseService
	{
		Task<List<LmsCourseRow>> GetCourseRowsAsync(LmsActor actor);

		Task<LmsCourse> GetCourseAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourseVersion>> GetVersionsAsync(int departmentId, int lmsCourseId);

		/// <summary>A version with its modules and lessons (department or system content).</summary>
		Task<LmsCourseOutline> GetOutlineAsync(int departmentId, int lmsCourseVersionId);

		Task<List<LmsCourseVersionApproval>> GetApprovalsAsync(int departmentId, int lmsCourseVersionId);

		Task<List<SignatureVerification>> GetApprovalSignaturesAsync(int departmentId, int lmsCourseVersionId);

		Task<List<LmsReviewItem>> GetReviewQueueAsync(LmsActor actor);

		Task<List<LmsCourseOwner>> GetOwnersAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourseAudience>> GetAudienceAsync(int departmentId, int lmsCourseId);

		Task<LmsCourseRights> GetRightsAsync(LmsActor actor, LmsCourse course);

		/// <summary>Creates a course with its first draft version and one module; the creator becomes an owner with every right.</summary>
		Task<LmsResult> CreateAsync(LmsActor actor, LmsCourseInput input, CancellationToken cancellationToken = default);

		Task<LmsResult> UpdateCourseAsync(LmsActor actor, int lmsCourseId, LmsCourseInput input, CancellationToken cancellationToken = default);

		Task<LmsResult> SaveVersionSettingsAsync(LmsActor actor, int lmsCourseVersionId, LmsVersionSettingsInput input, CancellationToken cancellationToken = default);

		/// <summary>Opens a draft copied from the version in force (modules and lessons included). Refused while an open draft exists.</summary>
		Task<LmsResult> StartDraftAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default);

		Task<LmsResult> DiscardDraftAsync(LmsActor actor, int lmsCourseVersionId, CancellationToken cancellationToken = default);

		Task<LmsResult> SaveModuleAsync(LmsActor actor, int lmsCourseVersionId, int lmsCourseModuleId, string title, CancellationToken cancellationToken = default);

		Task<LmsResult> DeleteModuleAsync(LmsActor actor, int lmsCourseModuleId, CancellationToken cancellationToken = default);

		Task<LmsResult> SaveLessonAsync(LmsActor actor, LmsLessonInput input, CancellationToken cancellationToken = default);

		Task<LmsResult> DeleteLessonAsync(LmsActor actor, int lmsLessonId, CancellationToken cancellationToken = default);

		/// <summary>Moves a lesson (or, with <paramref name="isModule"/>, a module) one place up or down in a draft.</summary>
		Task<LmsResult> MoveAsync(LmsActor actor, int lmsCourseVersionId, bool isModule, int id, bool up, CancellationToken cancellationToken = default);

		/// <summary>Draft → InReview (the shared lifecycle), the version's hash fixed for the approval to sign.</summary>
		Task<LmsResult> SubmitAsync(LmsActor actor, int lmsCourseVersionId, string changeSummary, CancellationToken cancellationToken = default);

		Task<LmsResult> WithdrawAsync(LmsActor actor, int lmsCourseVersionId, CancellationToken cancellationToken = default);

		/// <summary>Approves with an L2 signature bound to the version's content hash (the shared signing layer).</summary>
		Task<LmsResult> ApproveAsync(LmsActor actor, int lmsCourseVersionId, string comment, SignatureProof proof, CancellationToken cancellationToken = default);

		Task<LmsResult> RejectAsync(LmsActor actor, int lmsCourseVersionId, string comment, CancellationToken cancellationToken = default);

		/// <summary>
		/// Publishes an approved version (minor or major). Unstarted enrollments move to it; in-progress learners keep theirs (R-L12);
		/// on a major version completed learners are left alone, notified or re-assigned as <paramref name="policy"/> says.
		/// </summary>
		Task<LmsResult> PublishAsync(LmsActor actor, int lmsCourseVersionId, bool major, string changeSummary, LmsMajorChangePolicies policy, int? reassignDueDays,
			CancellationToken cancellationToken = default);

		Task<LmsResult> RetireAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default);

		// ---- T5 shared course sets (round 10 A3) ----

		/// <summary>Why a publisher's course version cannot be copied into another department (hosted video, packages, LTI…), or null.</summary>
		Task<string> GetSetCopyProblemAsync(int publisherDepartmentId, int lmsCourseVersionId);

		/// <summary>A subscriber's copy of a set course: a department course of its own whose first version (the publisher's, remapped) is published.</summary>
		Task<LmsResult> CreateSetCopyAsync(LmsSetCopyRequest request, CancellationToken cancellationToken = default);

		/// <summary>Publishes the publisher's newer version on a managed copy; learners who have not started move to it (R-L12).</summary>
		Task<LmsResult> PublishSetVersionAsync(LmsSetCopyRequest request, CancellationToken cancellationToken = default);

		Task<LmsResult> SaveOwnerAsync(LmsActor actor, int lmsCourseId, string userId, bool canEdit, bool canAssign, bool canReport, CancellationToken cancellationToken = default);

		Task<LmsResult> RemoveOwnerAsync(LmsActor actor, int lmsCourseId, int lmsCourseOwnerId, CancellationToken cancellationToken = default);
	}

	/// <summary>Question banks (T2 item 4): pools, tags, images, explanations, shared or private banks.</summary>
	public interface ILmsQuestionBankService
	{
		/// <summary>The banks the actor may use: shared ones and their own private ones (all for Manage Courses).</summary>
		Task<List<LmsQuestionBank>> GetBanksAsync(LmsActor actor);

		Task<LmsQuestionBank> GetBankAsync(LmsActor actor, int lmsQuestionBankId);

		Task<List<LmsQuestion>> GetQuestionsAsync(LmsActor actor, int lmsQuestionBankId);

		Task<LmsResult> SaveBankAsync(LmsActor actor, LmsQuestionBank bank, CancellationToken cancellationToken = default);

		Task<LmsResult> DeleteBankAsync(LmsActor actor, int lmsQuestionBankId, CancellationToken cancellationToken = default);

		/// <summary>Adds or edits a question (validated per kind: a correct option, true/false, two or more to order).</summary>
		Task<LmsResult> SaveQuestionAsync(LmsActor actor, LmsQuestion question, CancellationToken cancellationToken = default);

		/// <summary>Retires a question (kept for the attempts that drew it).</summary>
		Task<LmsResult> RetireQuestionAsync(LmsActor actor, int lmsQuestionId, CancellationToken cancellationToken = default);
	}

	/// <summary>Assessments (T2 item 4): attempts drawn and scored on the server only (R-T11), grading queue, item analysis, print.</summary>
	public interface ILmsAssessmentService
	{
		/// <summary>Starts (or resumes the open) attempt: random draw, revision hash and snapshot (R-T6). Enforces attempts and cooldown.</summary>
		Task<LmsAttemptView> StartAttemptAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, bool attested, string ipAddress, string userAgent,
			string locale, CancellationToken cancellationToken = default);

		Task<LmsAttemptView> GetAttemptAsync(int departmentId, string userId, int lmsAssessmentAttemptId);

		/// <summary>Scores on the server. Refused after the deadline (the attempt expires) or when already submitted.</summary>
		Task<LmsAttemptResultView> SubmitAttemptAsync(int departmentId, string userId, int lmsAssessmentAttemptId, IEnumerable<LmsAttemptAnswer> answers,
			CancellationToken cancellationToken = default);

		/// <summary>The finished attempt as the learner may see it (answer-review policy); the member's own, or any for a grader.</summary>
		Task<LmsAttemptResultView> GetResultAsync(LmsActor actor, int lmsAssessmentAttemptId);

		Task<List<LmsGradingItem>> GetGradingQueueAsync(LmsActor actor);

		Task<LmsAttemptResultView> GradeAsync(LmsActor actor, int lmsAssessmentAttemptId, IEnumerable<LmsManualGrade> grades, string notes,
			CancellationToken cancellationToken = default);

		Task<LmsItemAnalysisReport> GetItemAnalysisAsync(LmsActor actor, int lmsLessonId);

		/// <summary>A printable test from the lesson's pool (one draw); the key only for course editors.</summary>
		Task<LmsPrintableTest> GetPrintableTestAsync(LmsActor actor, int lmsLessonId, bool withKey);
	}

	/// <summary>The learner side (T2 items 2, 2a, 5, 6): catalog, enrollments, the player, progress, completion and surveys.</summary>
	public interface ILmsLearnerService : ILmsCourseResults
	{
		Task<List<LmsMyCourse>> GetMyCoursesAsync(int departmentId, string userId);

		Task<List<LmsCatalogEntry>> GetCatalogAsync(LmsActor actor);

		Task<LmsResult> EnrollSelfAsync(LmsActor actor, int lmsCourseId, CancellationToken cancellationToken = default);

		/// <summary>
		/// T5 (round 6E): enrols an external learner (an "xl-" id, never a member) in a published department course; the caller checks the
		/// course's external access and fair use. New enrolments need the add-on (lapse rule 3).
		/// </summary>
		Task<LmsResult> EnrollExternalAsync(int departmentId, int lmsCourseId, string learnerUserId, string actingUserId, bool selfEnrolled,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Security review M3 (2026-11-25): whether every protocol lesson of a course version may be shown to external learners under the
		/// public-page rules (<see cref="ProtocolPublicPages.Eligibility"/>: licensed content, audiences, document-backed, disabled or not in
		/// force); the first refusal, else Eligible. The player withholds a failing lesson from an external learner the same way.
		/// </summary>
		Task<ProtocolPublicEligibility> GetExternalProtocolEligibilityAsync(int departmentId, int lmsCourseVersionId);

		/// <summary>Assigns a course to the members the rules resolve to (active members only); needs the add-on for department courses.</summary>
		Task<LmsAssignOutcome> AssignAsync(LmsActor actor, int lmsCourseId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			LmsEnrollmentSources source = LmsEnrollmentSources.Assigned, string rolePathKey = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// An officer's own assignment (Courses → Assign, an Academy lesson): <see cref="AssignAsync"/> plus workflow trigger 271 per new
		/// enrollment. Requirements, paths and classes call <see cref="AssignAsync"/> and raise their own events.
		/// </summary>
		Task<LmsAssignOutcome> AssignDirectAsync(LmsActor actor, int lmsCourseId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default);

		Task<LmsResult> WithdrawEnrollmentAsync(LmsActor actor, int lmsEnrollmentId, CancellationToken cancellationToken = default);

		Task<List<LmsEnrollmentRow>> GetCourseEnrollmentsAsync(LmsActor actor, int lmsCourseId);

		/// <summary>The player page: the enrolled version's outline, progress and the lesson to show (resume when none is asked).</summary>
		Task<LmsPlayerView> GetPlayerAsync(int departmentId, string userId, int lmsEnrollmentId, int? lmsLessonId);

		/// <summary>Saves progress on every lesson change and heartbeat: seat time (capped by wall-clock), video position, watched percent.</summary>
		Task<LmsResult> SaveProgressAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, LmsProgressInput input,
			CancellationToken cancellationToken = default);

		/// <summary>Completes a lesson when its rules are met; completing the last required one completes the course.</summary>
		Task<LmsResult> CompleteLessonAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, LmsLessonCompletionInput input,
			CancellationToken cancellationToken = default);

		/// <summary>An anonymous survey response (no user id stored) and the lesson marked complete.</summary>
		Task<LmsResult> SubmitSurveyAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, IDictionary<string, string> answers,
			CancellationToken cancellationToken = default);

		Task<LmsSurveyReport> GetSurveyReportAsync(LmsActor actor, int lmsLessonId);

		/// <summary>Re-checks an enrollment after an attempt finished (pass completes the lesson, a passed pre-test the course).</summary>
		Task<bool> OnAttemptFinishedAsync(LmsAssessmentAttempt attempt, CancellationToken cancellationToken = default);

		/// <summary>Marks a member present at a crew viewing: enrolls them in the version shown and completes the lesson shown.</summary>
		Task<LmsResult> RecordCrewPresenceAsync(LmsCrewViewing viewing, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// T5: the runtime of a Scorm, Cmi5 or LtiLink lesson reported the learner's time and whether the lesson's rule is met (SCORM status,
		/// cmi5 moveOn, an LTI score at the pass mark). Runs on the course's gate and lapse rules like every other lesson; a satisfied lesson
		/// completes and may complete the course (<see cref="LmsResult.Count"/> 1). These kinds are never completed by the learner's button.
		/// </summary>
		Task<LmsResult> RecordRuntimeOutcomeAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, bool satisfied, int totalSeconds,
			CancellationToken cancellationToken = default);

		Task<LmsLessonFile> GetLessonFileAsync(LmsActor actor, int lmsLessonFileId);

		/// <summary>
		/// Round 10 (Omni): the files the member uploaded on one of their own enrollments (the file-request lessons' answers), metadata only
		/// (no bytes). Empty for another member's or a withdrawn enrollment.
		/// </summary>
		Task<List<LmsLessonFile>> GetMyLessonFilesAsync(int departmentId, string userId, int lmsEnrollmentId);

		/// <summary>
		/// Round 10 (Omni): one of the member's own uploaded files with its bytes, only when a lesson of one of their own (not withdrawn)
		/// enrollments holds it; with that enrollment and its course. Null otherwise (graders and course reporters use the web).
		/// </summary>
		Task<LmsMyLessonFile> GetMyLessonFileAsync(int departmentId, string userId, int lmsLessonFileId);

		/// <summary>Round 10 (Omni): an enrollment of the department by id (any learner), or null. The v4 signed media links resolve their learner through it.</summary>
		Task<LmsEnrollment> GetEnrollmentAsync(int departmentId, int lmsEnrollmentId);

		/// <summary>
		/// Issues the certification a completion grants, or renews the member's record of that type (T2 item 6; T3 paths reuse it). Returns
		/// the record id, or null when the type cannot be issued.
		/// </summary>
		Task<int?> IssueOrRenewCertificationAsync(int departmentId, string userId, int certificationTypeId, DateTime completedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Round 9A: as above, naming where it came from (a course or a path), the learning records behind it and who caused it, so the
		/// issuance ledger can take it back when one of those records is voided. The expiry follows the type's validity rule on the
		/// department's local date of the completion; a suspended or revoked record is never touched.
		/// </summary>
		Task<int?> IssueOrRenewCertificationAsync(int departmentId, string userId, int certificationTypeId, DateTime completedOnUtc, LmsCertificationIssueSources source,
			string sourceRefId, IReadOnlyCollection<int> basisRecordIds, string actorUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Round 7B (permission 135): completes the course's current version for one member (or a class roster's external learner) as an
		/// instructor recorded it: the open enrolment (or a new one) moves to the current version, every lesson is marked complete with the
		/// recording, and the normal completion follows (the LmsCourseCompletion learning record with the recorded-by metadata, the
		/// certification issued or renewed). The caller checked the permission, the member, the seat and the attestation, and inserted
		/// <paramref name="recording"/>. Refused when the course is not a published department course, the add-on does not allow new work, or
		/// the member already completed the current version (the first completion stands).
		/// </summary>
		Task<LmsResult> RecordCompletionForMemberAsync(int departmentId, int lmsCourseId, string userId, TrainingCompletionRecording recording,
			CancellationToken cancellationToken = default);

		// ---- worker 88 ---------------------------------------------------------------------------------------------------------

		/// <summary>Reminds learners of courses left in progress (idle 7 days), claimed before sending; a deep link opens the lesson.</summary>
		Task<int> SendInProgressRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Hosted video (decision 9, T2 item 3): uploads, captions, signed streaming, storage, offline manifest; worker 87 transcodes.</summary>
	public interface ILmsMediaService
	{
		bool IsStoreConfigured { get; }

		/// <summary>Stores an uploaded video and queues it for worker 87 (needs the add-on: lapse rule 6 makes hosted video read-only).</summary>
		Task<LmsResult> UploadVideoAsync(LmsActor actor, string fileName, string contentType, Stream content, long length, CancellationToken cancellationToken = default);

		/// <summary>Stores a WebVTT caption track on a draft video lesson.</summary>
		Task<LmsResult> UploadCaptionsAsync(LmsActor actor, int lmsLessonId, string locale, string label, byte[] webVtt, CancellationToken cancellationToken = default);

		Task<LmsMediaAsset> GetAssetAsync(int departmentId, int lmsMediaAssetId);

		Task<List<LmsMediaAsset>> GetAssetsAsync(int departmentId);

		/// <summary>
		/// The HLS playlist with every segment rewritten to a signed URL (or null when the asset is not playable: not ready, archived,
		/// or lapse rule 6). The caller checked the member may run the lesson.
		/// </summary>
		Task<string> GetSignedPlaylistAsync(LmsDepartmentGate gate, LmsMediaAsset asset, DateTime utcNow);

		/// <summary>A signed URL of the progressive MP4 rendition, or null.</summary>
		Task<string> GetSignedMp4UrlAsync(LmsDepartmentGate gate, LmsMediaAsset asset, DateTime utcNow);

		/// <summary>An uploaded caption track's text (WebVTT), or null.</summary>
		Task<string> GetCaptionsAsync(int departmentId, LmsLesson lesson, string locale);

		Task<LmsStorageReport> GetStorageReportAsync(int departmentId);

		/// <summary>The videos of the member's open enrollments that are marked available offline (the v4 manifest Omni would use, R-C9).</summary>
		Task<List<LmsOfflineVideo>> GetOfflineManifestAsync(int departmentId, string userId, DateTime utcNow);

		// ---- worker 87 / 88 ----------------------------------------------------------------------------------------------------

		/// <summary>Transcodes queued uploads to HLS (worker 87). Returns how many finished (ready or failed).</summary>
		Task<int> ProcessTranscodeQueueAsync(int maxItems, CancellationToken cancellationToken = default);

		/// <summary>Lapse rule 6 housekeeping: archives hosted media of departments lapsed more than 90 days (worker 88).</summary>
		Task<int> ArchiveLapsedMediaAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Crew viewing (R-C4): one lesson on a shared screen; members scan a rotating QR to be marked present.</summary>
	public interface ILmsCrewViewingService
	{
		Task<LmsResult> StartAsync(LmsActor actor, int lmsCourseId, int lmsLessonId, CancellationToken cancellationToken = default);

		Task<LmsCrewViewing> GetAsync(int departmentId, int lmsCrewViewingId);

		Task<List<LmsCrewViewingAttendee>> GetAttendeesAsync(int departmentId, int lmsCrewViewingId);

		/// <summary>The code to show now, or null when the viewing has ended or expired.</summary>
		string CurrentCode(LmsCrewViewing viewing, DateTime utcNow);

		/// <summary>A member's scan: the code must be current (or the one before), the viewing open and the member current.</summary>
		Task<LmsResult> ScanAsync(int departmentId, string userId, int lmsCrewViewingId, string code, string ipAddress, CancellationToken cancellationToken = default);

		Task<LmsResult> EndAsync(LmsActor actor, int lmsCrewViewingId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// What a department has (modules, feature flags, add-ons) for Academy visibility (R-S2 "visibility follows entitlement"). Add-ons
	/// in one billing call per request, and only when a lesson needs one; fails closed (an add-on lesson is hidden on any error).
	/// </summary>
	public interface ILmsEntitlementService
	{
		Task<AcademyEntitlements> GetEntitlementsAsync(int departmentId, IEnumerable<string> visibilityKeys);

		/// <summary>Whether a course is visible to the department (no keys: always).</summary>
		Task<bool> IsVisibleAsync(int departmentId, LmsCourse course);
	}

	/// <summary>The Resgrid Academy as a system course set (A1): seeding, the catalog by entitlement, lessons and role paths assigned.</summary>
	public interface ILmsAcademyService
	{
		/// <summary>
		/// Seeds (or re-seeds) the system courses from <see cref="AcademyCourseManifest"/>: idempotent by SystemKey; a higher manifest
		/// version publishes a new minor version of each course. Returns the number of courses created or updated.
		/// </summary>
		Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default);

		Task<AcademyCatalogView> GetCatalogAsync(LmsActor actor);

		/// <summary>The system course of an Academy lesson id, or null.</summary>
		Task<LmsCourse> GetCourseForLessonAsync(string lessonId);

		/// <summary>Assigns one Academy lesson ("Assign this lesson"); free, Assign Training (126) or Manage Courses.</summary>
		Task<LmsAssignOutcome> AssignLessonAsync(LmsActor actor, string lessonId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>Assigns a role path: the list is recorded per member and each visible lesson is enrolled.</summary>
		Task<LmsAssignOutcome> AssignPathAsync(LmsActor actor, string pathKey, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default);

		Task<List<AcademyPathProgressRow>> GetPathProgressAsync(LmsActor actor, string pathKey);

		/// <summary>The localized text of an Academy resource ("{lessonId}.Title", "Path.member"…), English when the locale lacks it.</summary>
		string Text(string key, string locale);
	}
}
