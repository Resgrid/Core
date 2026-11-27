using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T4 part 1 (round 5B, M0378–M0380): class sessions, rosters, instructors, cost lines, lesson plans, training resources and
	// bookings. Every read is department scoped; the worker 95 reads are due-work queries (no per-department sweep).

	public interface ILmsClassSessionsRepository : IRepository<LmsClassSession>
	{
		Task<LmsClassSession> GetByIdForDepartmentAsync(int departmentId, int lmsClassSessionId);
		/// <summary>Sessions overlapping the window (either bound may be open), not deleted unless asked.</summary>
		Task<List<LmsClassSession>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, bool includeDeleted = false);
		Task<List<LmsClassSession>> GetBySeriesAsync(int departmentId, string seriesKey);
		Task<LmsClassSession> GetByCalendarItemAsync(int departmentId, int calendarItemId);
		/// <summary>Worker 95: scheduled sessions starting within <paramref name="horizonUtc"/> that ask for a reminder and still have an enrolled member not reminded.</summary>
		Task<List<LmsClassSession>> GetReminderCandidatesAsync(DateTime utcNow, DateTime horizonUtc, int take);
		/// <summary>Worker 95: scheduled future sessions with a waitlist that has someone on it.</summary>
		Task<List<LmsClassSession>> GetWaitlistCandidatesAsync(DateTime utcNow, int take);
		/// <summary>Worker 95: deleted sessions deleted before <paramref name="deletedBeforeUtc"/> whose retention has passed (or which have none).</summary>
		Task<List<LmsClassSession>> GetPurgeCandidatesAsync(DateTime utcNow, DateTime deletedBeforeUtc, int take);
		/// <summary>
		/// Deletes the session's roster, instructors, cost lines and bookings and the session itself in one transaction, only when it is
		/// deleted and its retention has passed (the guard is in the statement). False when the hold refused it.
		/// </summary>
		Task<bool> PurgeAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsClassEnrollmentsRepository : IRepository<LmsClassEnrollment>
	{
		Task<LmsClassEnrollment> GetByIdForDepartmentAsync(int departmentId, int lmsClassEnrollmentId);
		Task<List<LmsClassEnrollment>> GetForSessionAsync(int departmentId, int lmsClassSessionId);
		Task<List<LmsClassEnrollment>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds);
		Task<LmsClassEnrollment> GetForMemberAsync(int departmentId, int lmsClassSessionId, string userId);
		Task<List<LmsClassEnrollment>> GetForUserAsync(int departmentId, string userId);
		/// <summary>Claims the reminder (sets ReminderSentOn when still empty); true for the one caller that claimed it.</summary>
		Task<bool> ClaimReminderAsync(int departmentId, int lmsClassEnrollmentId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Waitlisted → enrolled, only while still waitlisted; true when this call promoted it.</summary>
		Task<bool> PromoteAsync(int departmentId, int lmsClassEnrollmentId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Round 7B: the class moved, so every reminder claim of its roster is cleared (worker 95 reminds again for the new time).</summary>
		Task<int> ResetRemindersAsync(int departmentId, int lmsClassSessionId, CancellationToken cancellationToken = default);
	}

	public interface ILmsClassInstructorsRepository : IRepository<LmsClassInstructor>
	{
		Task<LmsClassInstructor> GetByIdForDepartmentAsync(int departmentId, int lmsClassInstructorId);
		Task<List<LmsClassInstructor>> GetForSessionAsync(int departmentId, int lmsClassSessionId);
		Task<List<LmsClassInstructor>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds);
		Task<List<LmsClassInstructor>> GetForUserAsync(int departmentId, string userId);
	}

	public interface ILmsClassCostLinesRepository : IRepository<LmsClassCostLine>
	{
		Task<LmsClassCostLine> GetByIdForDepartmentAsync(int departmentId, int lmsClassCostLineId);
		Task<List<LmsClassCostLine>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds);
	}

	public interface ILmsTrainingResourcesRepository : IRepository<LmsTrainingResource>
	{
		Task<LmsTrainingResource> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingResourceId);
		Task<List<LmsTrainingResource>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);
	}

	public interface ILmsClassResourceBookingsRepository : IRepository<LmsClassResourceBooking>
	{
		Task<LmsClassResourceBooking> GetByIdForDepartmentAsync(int departmentId, int lmsClassResourceBookingId);
		Task<List<LmsClassResourceBooking>> GetForSessionAsync(int departmentId, int lmsClassSessionId);
		Task<List<LmsClassResourceBooking>> GetForResourceAsync(int departmentId, int lmsTrainingResourceId, DateTime fromUtc, DateTime toUtc);
		/// <summary>Another booking of the resource overlaps [start, end) (bookings of <paramref name="exceptSessionId"/> aside).</summary>
		Task<bool> HasOverlapAsync(int departmentId, int lmsTrainingResourceId, DateTime startUtc, DateTime endUtc, int exceptSessionId);
		Task<int> DeleteForSessionAsync(int departmentId, int lmsClassSessionId, CancellationToken cancellationToken = default);
	}

	public interface ILmsLessonPlansRepository : IRepository<LmsLessonPlan>
	{
		Task<LmsLessonPlan> GetByIdForDepartmentAsync(int departmentId, int lmsLessonPlanId);
		Task<List<LmsLessonPlan>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);
	}

	public interface ILmsLessonPlanVersionsRepository : IRepository<LmsLessonPlanVersion>
	{
		Task<LmsLessonPlanVersion> GetByIdForDepartmentAsync(int departmentId, int lmsLessonPlanVersionId);
		Task<List<LmsLessonPlanVersion>> GetForPlanAsync(int departmentId, int lmsLessonPlanId);
		/// <summary>Moves a version from one status to another only when it still has the first (optimistic transition); true when moved.</summary>
		Task<bool> MoveAsync(int departmentId, int lmsLessonPlanVersionId, int fromStatus, int toStatus, CancellationToken cancellationToken = default);
	}
}
