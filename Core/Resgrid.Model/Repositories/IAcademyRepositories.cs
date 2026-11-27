using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Academy;

namespace Resgrid.Model.Repositories
{
	/// <summary>A3 guidance remembered per member (AcademyGuidanceStates, M0375).</summary>
	public interface IAcademyGuidanceStatesRepository : IRepository<AcademyGuidanceState>
	{
		/// <summary>Every guidance state the member has in this department (a handful of rows).</summary>
		Task<List<AcademyGuidanceState>> GetForUserAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Inserts the member's state for its key, or replaces the existing one (unique per department, member and key).</summary>
		Task<AcademyGuidanceState> UpsertAsync(AcademyGuidanceState state, CancellationToken cancellationToken = default);
	}

	/// <summary>Major Academy lesson revisions waiting for worker 88 (AcademyLessonUpdates, M0375; system-level).</summary>
	public interface IAcademyLessonUpdatesRepository : IRepository<AcademyLessonUpdate>
	{
		/// <summary>Records a major revision once; false when that course and revision are already recorded.</summary>
		Task<bool> InsertIfNewAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default);

		/// <summary>Updates not yet completed, oldest first.</summary>
		Task<List<AcademyLessonUpdate>> GetPendingAsync(int take, CancellationToken cancellationToken = default);

		/// <summary>Saves the cursor and completion of one update.</summary>
		Task<bool> SaveProgressAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default);

		/// <summary>
		/// Departments above <paramref name="afterDepartmentId"/>, ascending, that require the lesson: a live learning requirement (T3) on the
		/// course, or on a path with a step for it (round 5F; assignments and self-enrolment alone do not make a lesson required).
		/// </summary>
		Task<List<int>> GetAssigningDepartmentIdsAsync(int lmsCourseId, int afterDepartmentId, int take, CancellationToken cancellationToken = default);

		/// <summary>The department's live requirements on the lesson (a Course requirement on it, or a Path requirement whose path has a step for it).</summary>
		Task<List<AcademyLessonRequirement>> GetRequirementsOnLessonAsync(int departmentId, int lmsCourseId, CancellationToken cancellationToken = default);
	}
}
