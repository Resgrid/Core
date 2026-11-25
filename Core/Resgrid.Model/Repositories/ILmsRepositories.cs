using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// LMS courses and assessments (Training T2, M0359/M0360). Reads begin at the department; a read that may also return
	// system-published content (DepartmentId 0, the Academy) says so. Baseline repositories never reference these (§17.3).

	public interface ILmsCoursesRepository : IRepository<LmsCourse>
	{
		/// <summary>The department's own courses (never system content), deleted ones only when asked.</summary>
		Task<List<LmsCourse>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		/// <summary>Every system-published course (the Academy).</summary>
		Task<List<LmsCourse>> GetSystemCoursesAsync();

		/// <summary>A course of the department, or a system course (DepartmentId 0).</summary>
		Task<LmsCourse> GetVisibleByIdAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourse>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseIds);

		Task<LmsCourse> GetBySystemKeyAsync(string systemKey);

		/// <summary>Writes the course only if its RowVersion is still <paramref name="expectedRowVersion"/>, and bumps it.</summary>
		Task<bool> TryUpdateAsync(LmsCourse course, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsCourseVersionsRepository : IRepository<LmsCourseVersion>
	{
		/// <summary>The course's versions, newest first (department or system).</summary>
		Task<List<LmsCourseVersion>> GetForCourseAsync(int departmentId, int lmsCourseId);

		/// <summary>A version of the department, or of a system course.</summary>
		Task<LmsCourseVersion> GetVisibleByIdAsync(int departmentId, int lmsCourseVersionId);

		Task<List<LmsCourseVersion>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseVersionIds);

		/// <summary>Any version by id, whatever its department (the P2 pass check, which knows only the user and the version).</summary>
		Task<LmsCourseVersion> GetAnyByIdAsync(int lmsCourseVersionId);

		/// <summary>Versions of the department waiting for approval (InReview).</summary>
		Task<List<LmsCourseVersion>> GetInReviewAsync(int departmentId);

		Task<bool> TryUpdateAsync(LmsCourseVersion version, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsCourseVersionApprovalsRepository : IRepository<LmsCourseVersionApproval>
	{
		Task<List<LmsCourseVersionApproval>> GetForVersionAsync(int departmentId, int lmsCourseVersionId);

		/// <summary>Decides a pending step only while it is still pending (two approvers cannot decide one step).</summary>
		Task<bool> TryDecideAsync(LmsCourseVersionApproval step, CancellationToken cancellationToken = default);

		Task<int> WithdrawPendingAsync(int departmentId, int lmsCourseVersionId, int round, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsCourseAudiencesRepository : IRepository<LmsCourseAudience>
	{
		Task<List<LmsCourseAudience>> GetForCourseAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourseAudience>> GetForDepartmentAsync(int departmentId);

		/// <summary>Replaces the course's audience rows with <paramref name="rules"/> in one transaction.</summary>
		Task ReplaceAsync(int departmentId, int lmsCourseId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default);
	}

	public interface ILmsCourseOwnersRepository : IRepository<LmsCourseOwner>
	{
		Task<List<LmsCourseOwner>> GetForCourseAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourseOwner>> GetForUserAsync(int departmentId, string userId);
	}

	public interface ILmsCourseModulesRepository : IRepository<LmsCourseModule>
	{
		Task<List<LmsCourseModule>> GetForVersionAsync(int departmentId, int lmsCourseVersionId);

		Task<LmsCourseModule> GetVisibleByIdAsync(int departmentId, int lmsCourseModuleId);
	}

	public interface ILmsLessonsRepository : IRepository<LmsLesson>
	{
		Task<List<LmsLesson>> GetForVersionAsync(int departmentId, int lmsCourseVersionId);

		Task<LmsLesson> GetVisibleByIdAsync(int departmentId, int lmsLessonId);
	}

	public interface ILmsQuestionBanksRepository : IRepository<LmsQuestionBank>
	{
		Task<List<LmsQuestionBank>> GetForDepartmentAsync(int departmentId);

		Task<LmsQuestionBank> GetVisibleByIdAsync(int departmentId, int lmsQuestionBankId);

		Task<LmsQuestionBank> GetBySystemKeyAsync(string systemKey);
	}

	public interface ILmsQuestionsRepository : IRepository<LmsQuestion>
	{
		/// <summary>The bank's questions (retired included), in order.</summary>
		Task<List<LmsQuestion>> GetForBankAsync(int departmentId, int lmsQuestionBankId);

		Task<LmsQuestion> GetVisibleByIdAsync(int departmentId, int lmsQuestionId);
	}

	public interface ILmsAssessmentAttemptsRepository : IRepository<LmsAssessmentAttempt>
	{
		Task<LmsAssessmentAttempt> GetByIdForDepartmentAsync(int departmentId, int lmsAssessmentAttemptId);

		/// <summary>The learner's attempts on a lesson of an enrollment, newest first.</summary>
		Task<List<LmsAssessmentAttempt>> GetForEnrollmentLessonAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId);

		/// <summary>The member's attempts of the department on the given lessons (any enrollment), newest first.</summary>
		Task<List<LmsAssessmentAttempt>> GetForUserLessonsAsync(int departmentId, string userId, IEnumerable<int> lmsLessonIds);

		/// <summary>Attempts waiting for a grader, oldest first.</summary>
		Task<List<LmsAssessmentAttempt>> GetPendingGradingAsync(int departmentId);

		/// <summary>
		/// Every attempt of the member in the department, newest first, for the GDPR export (round 5F): without the snapshot, so the drawn
		/// questions' keys never leave the database on this path.
		/// </summary>
		Task<List<LmsAssessmentAttempt>> GetForUserWithoutSnapshotAsync(int departmentId, string userId);

		/// <summary>Scored and graded attempts of a lesson (item analysis).</summary>
		Task<List<LmsAssessmentAttempt>> GetFinishedForLessonAsync(int departmentId, int lmsLessonId);

		Task<bool> TryUpdateAsync(LmsAssessmentAttempt attempt, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsEnrollmentsRepository : IRepository<LmsEnrollment>
	{
		Task<LmsEnrollment> GetByIdForDepartmentAsync(int departmentId, int lmsEnrollmentId);

		/// <summary>The member's open enrollment (NotStarted or InProgress) in a course, if any.</summary>
		Task<LmsEnrollment> GetOpenAsync(int departmentId, string userId, int lmsCourseId);

		Task<List<LmsEnrollment>> GetForUserAsync(int departmentId, string userId);

		Task<List<LmsEnrollment>> GetForCourseAsync(int departmentId, int lmsCourseId);

		/// <summary>Every enrollment of the department in the given courses (Academy path progress).</summary>
		Task<List<LmsEnrollment>> GetForCoursesAsync(int departmentId, IEnumerable<int> lmsCourseIds);

		/// <summary>Open enrollments of a course across departments (system courses re-pin unstarted ones on a new version).</summary>
		Task<List<LmsEnrollment>> GetOpenForCourseAnyDepartmentAsync(int lmsCourseId);

		/// <summary>
		/// Inserts an open enrollment unless the member already has one for the course (the filtered unique index decides, so two
		/// concurrent assignments make one row). Returns the stored open enrollment and whether this call created it.
		/// </summary>
		Task<(LmsEnrollment Enrollment, bool Created)> InsertOpenAsync(LmsEnrollment enrollment, CancellationToken cancellationToken = default);

		Task<bool> TryUpdateAsync(LmsEnrollment enrollment, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Whether the member completed the given version (any department).</summary>
		Task<bool> HasCompletedVersionAsync(string userId, int lmsCourseVersionId);

		/// <summary>
		/// In-progress enrollments idle since before <paramref name="idleBeforeUtc"/> and not reminded since then (worker 88), across
		/// departments, oldest first.
		/// </summary>
		Task<List<LmsEnrollment>> GetReminderDueAsync(DateTime idleBeforeUtc, int take);

		/// <summary>Claims the reminder (sets ReminderSentOn) only if no other run did; false when already claimed.</summary>
		Task<bool> ClaimReminderAsync(int departmentId, int lmsEnrollmentId, DateTime? previousReminderOn, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsLessonProgressRepository : IRepository<LmsLessonProgress>
	{
		Task<List<LmsLessonProgress>> GetForEnrollmentAsync(int departmentId, int lmsEnrollmentId);

		Task<LmsLessonProgress> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId);
	}

	public interface ILmsLessonFilesRepository : IRepository<LmsLessonFile>
	{
		Task<LmsLessonFile> GetByIdForDepartmentAsync(int departmentId, int lmsLessonFileId);
	}

	public interface ILmsSurveyResponsesRepository : IRepository<LmsSurveyResponse>
	{
		Task<List<LmsSurveyResponse>> GetForLessonAsync(int departmentId, int lmsLessonId);
	}

	public interface ILmsCrewViewingsRepository : IRepository<LmsCrewViewing>
	{
		Task<LmsCrewViewing> GetByIdForDepartmentAsync(int departmentId, int lmsCrewViewingId);
	}

	public interface ILmsCrewViewingAttendeesRepository : IRepository<LmsCrewViewingAttendee>
	{
		Task<List<LmsCrewViewingAttendee>> GetForViewingAsync(int departmentId, int lmsCrewViewingId);

		/// <summary>Marks the member present once (unique per viewing); false when they already were.</summary>
		Task<bool> TryInsertAsync(LmsCrewViewingAttendee attendee, CancellationToken cancellationToken = default);
	}

	public interface ILmsPathAssignmentsRepository : IRepository<LmsPathAssignment>
	{
		Task<List<LmsPathAssignment>> GetForDepartmentAsync(int departmentId);

		Task<List<LmsPathAssignment>> GetForUserAsync(int departmentId, string userId);

		/// <summary>T3: links every department's unlinked assignments of an Academy role path to its system path (the A1 ordered-list migration).</summary>
		Task<int> LinkSystemPathAsync(string pathKey, int lmsPathId, CancellationToken cancellationToken = default);

		/// <summary>T3: stamps a finished path once (compare-and-set on CompletedOn).</summary>
		Task<bool> TryCompleteAsync(int departmentId, int lmsPathAssignmentId, DateTime completedOnUtc, int? personnelCertificationId, CancellationToken cancellationToken = default);
	}

	public interface ILmsDepartmentStatesRepository : IRepository<LmsDepartmentState>
	{
		Task<LmsDepartmentState> GetForDepartmentAsync(int departmentId);

		/// <summary>Records the add-on as seen active now (insert or update).</summary>
		Task StampEntitledAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Departments with Ready hosted media whose add-on was last seen before <paramref name="entitledBeforeUtc"/> (worker 88).</summary>
		Task<List<int>> GetLapsedWithReadyMediaAsync(DateTime entitledBeforeUtc);
	}

	public interface ILmsMediaAssetsRepository : IRepository<LmsMediaAsset>
	{
		Task<LmsMediaAsset> GetByIdForDepartmentAsync(int departmentId, int lmsMediaAssetId);

		Task<List<LmsMediaAsset>> GetForDepartmentAsync(int departmentId);

		/// <summary>
		/// Claims the oldest queued asset (or one whose Transcoding claim is older than <paramref name="staleClaimBeforeUtc"/>) for worker 87:
		/// moves it to Transcoding with ClaimedOn now and Attempts + 1. Null when there is no work.
		/// </summary>
		Task<LmsMediaAsset> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default);

		/// <summary>Moves an asset from <paramref name="from"/> only (a compare-and-set on Status), writing the asset's other fields.</summary>
		Task<bool> TryMoveAsync(LmsMediaAsset asset, LmsMediaStatuses from, CancellationToken cancellationToken = default);

		/// <summary>Stored bytes (sources and renditions) of the department's media that is not archived.</summary>
		Task<long> GetStoredBytesAsync(int departmentId);
	}
}
