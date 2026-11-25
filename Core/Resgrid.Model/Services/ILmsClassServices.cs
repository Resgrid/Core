using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T4 part 1 (round 5B): LMS add-on services. Their callers check the LMS gate (ILmsGateService) first; worker 95's entry point
	// queries for due work first and asks the gate once per department with due work (R-L9). Refusals are InvalidOperationException
	// ("lms_class_*") codes the pages show as text.

	/// <summary>Class sessions on the Calendar (§13 T4 item 1, §13a T4 Events): enrolment, waitlist, roster, sign-in, sign-off, costs, booking.</summary>
	public interface ILmsClassService
	{
		Task<List<LmsClassSession>> GetSessionsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);
		Task<LmsClassSession> GetSessionAsync(int departmentId, int lmsClassSessionId);
		Task<LmsClassSession> GetSessionForCalendarItemAsync(int departmentId, int calendarItemId);
		Task<LmsClassSessionDetail> GetDetailAsync(int departmentId, int lmsClassSessionId, DateTime utcNow);
		Task<List<LmsClassSession>> GetSeriesAsync(int departmentId, string seriesKey);
		Task<List<LmsClassEnrollment>> GetEnrollmentsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds);
		/// <summary>The member's classes (on the roster or teaching) and the visible ones they may still enrol in.</summary>
		Task<List<LmsMyClass>> GetMyClassesAsync(int departmentId, string userId, DateTime utcNow);
		Task<bool> IsInstructorAsync(int departmentId, int lmsClassSessionId, string userId);
		/// <summary>Instructor records (item 4): what the member taught.</summary>
		Task<List<LmsClassInstructor>> GetTaughtAsync(int departmentId, string userId);
		Task<List<LmsClassInstructor>> GetInstructorsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds);

		/// <summary>Creates the session (or each session of a series) with its calendar item; returns them in order.</summary>
		Task<List<LmsClassSession>> CreateAsync(int departmentId, LmsClassSessionInput input, string userId, CancellationToken cancellationToken = default);
		Task<LmsClassSession> UpdateAsync(int departmentId, int lmsClassSessionId, LmsClassSessionInput input, string userId, CancellationToken cancellationToken = default);
		/// <summary>The round-4C seam: an annual plan line becomes a class session (and its calendar item); the line keeps both ids.</summary>
		Task<LmsClassSession> CreateFromPlanItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default);
		Task<LmsClassSession> CancelAsync(int departmentId, int lmsClassSessionId, string reason, string userId, CancellationToken cancellationToken = default);
		/// <summary>Soft delete: the roster stays until worker 95 purges it after its retention (never before).</summary>
		Task<bool> DeleteAsync(int departmentId, int lmsClassSessionId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Removes a deleted session's rows now; refused while its retention holds (lms_class_retention_hold).</summary>
		Task<bool> PurgeAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<LmsClassSession> RotateQrAsync(int departmentId, int lmsClassSessionId, string userId, CancellationToken cancellationToken = default);

		Task<LmsClassInstructor> AddInstructorAsync(int departmentId, int lmsClassSessionId, LmsClassInstructor instructor, string userId, CancellationToken cancellationToken = default);
		Task<bool> RemoveInstructorAsync(int departmentId, int lmsClassInstructorId, string userId, CancellationToken cancellationToken = default);
		Task<LmsClassCostLine> AddCostAsync(int departmentId, int lmsClassSessionId, LmsClassCostLine line, string userId, CancellationToken cancellationToken = default);
		Task<bool> RemoveCostAsync(int departmentId, int lmsClassCostLineId, string userId, CancellationToken cancellationToken = default);

		Task<List<LmsTrainingResource>> GetResourcesAsync(int departmentId);
		Task<LmsTrainingResource> SaveResourceAsync(LmsTrainingResource resource, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteResourceAsync(int departmentId, int lmsTrainingResourceId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Books a resource for the session's time; a booking that overlaps another of the same resource is refused.</summary>
		Task<LmsClassResourceBooking> BookResourceAsync(int departmentId, int lmsClassSessionId, int lmsTrainingResourceId, string userId, CancellationToken cancellationToken = default);
		Task<bool> CancelBookingAsync(int departmentId, int lmsClassResourceBookingId, string userId, CancellationToken cancellationToken = default);
		Task<List<LmsClassResourceBooking>> GetBookingsAsync(int departmentId, int lmsTrainingResourceId, DateTime fromUtc, DateTime toUtc);

		/// <summary>A member enrols themselves under the session's rules (open, cutoff, groups, approval, capacity and waitlist).</summary>
		Task<LmsClassEnrollment> EnrollSelfAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>A class manager enrols a member (no approval; over capacity only when there is no waitlist).</summary>
		Task<LmsClassEnrollment> EnrollMemberAsync(int departmentId, int lmsClassSessionId, string memberUserId, string managerUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> UnenrollSelfAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> RemoveMemberAsync(int departmentId, int lmsClassEnrollmentId, string managerUserId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> DecideAsync(int departmentId, int lmsClassEnrollmentId, bool approve, string managerUserId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Moves waitlisted active members into free seats, in order; returns how many were promoted.</summary>
		Task<int> PromoteWaitlistAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The current rotating code of the session's QR (null when QR sign-in is off or closed).</summary>
		string CurrentCode(LmsClassSession session, DateTime utcNow);
		/// <summary>A member's QR sign-in: the code must be current (an old one is refused); GPS only when the session requires it.</summary>
		Task<LmsClassEnrollment> SignInAsync(int departmentId, int lmsClassSessionId, string userId, string code, decimal? latitude, decimal? longitude, DateTime utcNow,
			CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> SignOutAsync(int departmentId, int lmsClassSessionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> MarkAttendanceAsync(int departmentId, int lmsClassEnrollmentId, LmsClassAttendanceStates state, decimal? hoursOverride, string actingUserId,
			DateTime utcNow, CancellationToken cancellationToken = default);
		Task<LmsClassEnrollment> AddWalkInAsync(int departmentId, int lmsClassSessionId, string memberUserId, string actingUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);
		/// <summary>Teams / Zoom / Meet attendance report: matches people by e-mail, then name; marks them attended with the minutes.</summary>
		Task<LmsAttendanceImportResult> ImportAttendanceAsync(int departmentId, int lmsClassSessionId, string reportText, string actingUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);
		/// <summary>
		/// Instructor sign-off: snapshots the roster (names, job titles, hours), writes one ClassSession learning record per attendee and per
		/// member instructor (none for the absent), idempotently, sets the retention hold, offers the hours to the timecard and starts the
		/// post-class evaluation. Signing again rewrites only what changed.
		/// </summary>
		Task<LmsClassSignOffResult> SignOffAsync(int departmentId, int lmsClassSessionId, string signerUserId, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<HashSet<string>> GetShiftConflictsAsync(int departmentId, LmsClassSession session, IEnumerable<string> userIds);
		Task<List<LmsClassShiftConflict>> GetUpcomingConflictsAsync(int departmentId, DateTime utcNow, int days);
		Task<LmsClassCostRollup> GetCostRollupAsync(int departmentId, DateTime fromUtc, DateTime toUtc, LmsClassCostGroupings groupBy);
	}

	/// <summary>Lesson plans on the content core (§13 T4 item 5, R-C6): draft, submit, approve, publish; Resgrid starter plans.</summary>
	public interface ILmsLessonPlanService
	{
		IReadOnlyList<LmsLessonPlanStarter> GetStarters();
		Task<List<LmsLessonPlan>> GetPlansAsync(int departmentId);
		Task<LmsLessonPlan> GetPlanAsync(int departmentId, int lmsLessonPlanId);
		Task<List<LmsLessonPlanVersion>> GetVersionsAsync(int departmentId, int lmsLessonPlanId);
		Task<LmsLessonPlanVersion> GetVersionAsync(int departmentId, int lmsLessonPlanVersionId);
		Task<LmsLessonPlan> CreateAsync(int departmentId, string title, LmsLessonPlanContent content, string userId, CancellationToken cancellationToken = default);
		/// <summary>Copies a starter plan into a new department draft.</summary>
		Task<LmsLessonPlan> ApplyStarterAsync(int departmentId, string starterKey, string userId, CancellationToken cancellationToken = default);
		Task<LmsLessonPlanVersion> SaveDraftAsync(int departmentId, int lmsLessonPlanVersionId, string title, LmsLessonPlanContent content, string userId,
			CancellationToken cancellationToken = default);
		Task<LmsLessonPlanVersion> SubmitAsync(int departmentId, int lmsLessonPlanVersionId, string changeSummary, string userId, CancellationToken cancellationToken = default);
		Task<LmsLessonPlanVersion> DecideAsync(int departmentId, int lmsLessonPlanVersionId, bool approve, string note, string userId, CancellationToken cancellationToken = default);
		/// <summary>Publishes an approved version (1.0 first, then major or minor); the previous published version is superseded.</summary>
		Task<LmsLessonPlanVersion> PublishAsync(int departmentId, int lmsLessonPlanVersionId, bool major, string userId, CancellationToken cancellationToken = default);
		/// <summary>Opens a new draft from the published version (one draft at a time).</summary>
		Task<LmsLessonPlanVersion> NewDraftAsync(int departmentId, int lmsLessonPlanId, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteAsync(int departmentId, int lmsLessonPlanId, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>Worker 95: class reminders, waitlist promotion and the roster retention purge (due-work queries first, the gate once per department).</summary>
	public interface ILmsClassSweepService
	{
		Task<LmsClassSweepResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Direct class notifications (EventTypes 64–65): only active members, in each recipient's language; returns the members notified.</summary>
	public interface ILmsClassNotifier
	{
		Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string titleKey, string messageKey, Func<CultureInfo, object[]> args, string eventCode,
			CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The post-class evaluation seam (§13 T4 item 1): an implementation per <see cref="LmsClassPostEvaluationKinds"/> starts the evaluation
	/// for the attendees at sign-off. This stream registers CourseAssessment (assigns the linked LMS course); the skill-sheet stream
	/// registers SkillSheet. Returns how many evaluations were started.
	/// </summary>
	public interface ILmsPostClassEvaluation
	{
		LmsClassPostEvaluationKinds Kind { get; }
		Task<int> StartAsync(LmsActor signer, LmsClassSession session, IReadOnlyCollection<string> attendeeUserIds, CancellationToken cancellationToken = default);
	}
}
