using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Academy;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.Academy
{
	/// <summary>
	/// In-memory AcademyLessonUpdates with the Dapper repository's rules (unique per course and revision; the departments that require a lesson
	/// read from live learning requirements on it, directly or through a path step, round 5F).
	/// </summary>
	public sealed class FakeAcademyLessonUpdates : MemRepo<AcademyLessonUpdate>, IAcademyLessonUpdatesRepository
	{
		private readonly FakeLmsEnrollments _enrollments;

		/// <summary>The departments' learning requirements (T3) the cursor and the department's choice are read from.</summary>
		public List<LmsRequirement> Requirements { get; } = new List<LmsRequirement>();

		/// <summary>Path steps (Path requirements cover the lessons on their path).</summary>
		public List<LmsPathStep> PathSteps { get; } = new List<LmsPathStep>();

		public FakeAcademyLessonUpdates(FakeLmsEnrollments enrollments)
		{
			_enrollments = enrollments;
		}

		public async Task<bool> InsertIfNewAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default)
		{
			if (Rows.Any(r => r.LmsCourseId == update.LmsCourseId && r.Revision == update.Revision))
				return false;
			await InsertAsync(update, cancellationToken);
			return true;
		}

		public Task<List<AcademyLessonUpdate>> GetPendingAsync(int take, CancellationToken cancellationToken = default) =>
			Task.FromResult(Out(Rows.Where(r => !r.CompletedOn.HasValue).OrderBy(r => r.AcademyLessonUpdateId).Take(take)));

		public async Task<bool> SaveProgressAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default)
		{
			await UpdateAsync(update, cancellationToken);
			return true;
		}

		private bool RequiresLesson(LmsRequirement r, int lmsCourseId) =>
			r.IsLive && ((r.Kind == (int)LmsRequirementKinds.Course && r.TargetRefId == lmsCourseId) ||
				(r.Kind == (int)LmsRequirementKinds.Path && PathSteps.Any(s => s.LmsPathId == r.TargetRefId && s.Kind == (int)LmsPathStepKinds.Course && s.TargetRefId == lmsCourseId)));

		public Task<List<int>> GetAssigningDepartmentIdsAsync(int lmsCourseId, int afterDepartmentId, int take, CancellationToken cancellationToken = default) =>
			Task.FromResult(Requirements.Where(r => r.DepartmentId > afterDepartmentId && r.DepartmentId > 0 && RequiresLesson(r, lmsCourseId))
				.Select(r => r.DepartmentId).Distinct().OrderBy(d => d).Take(take).ToList());

		public Task<List<AcademyLessonRequirement>> GetRequirementsOnLessonAsync(int departmentId, int lmsCourseId, CancellationToken cancellationToken = default) =>
			Task.FromResult(Requirements.Where(r => r.DepartmentId == departmentId && RequiresLesson(r, lmsCourseId))
				.Select(r => new AcademyLessonRequirement { LmsRequirementId = r.LmsRequirementId, DepartmentId = r.DepartmentId, AcademyUpdatePolicy = r.AcademyUpdatePolicy, DueDays = r.DueDays })
				.ToList());
	}

	/// <summary>In-memory AcademyGuidanceStates: one row per department, member (case-insensitive) and key.</summary>
	public sealed class FakeAcademyGuidanceStates : MemRepo<AcademyGuidanceState>, IAcademyGuidanceStatesRepository
	{
		public Task<List<AcademyGuidanceState>> GetForUserAsync(int departmentId, string userId, CancellationToken cancellationToken = default) =>
			Task.FromResult(Out(Rows.Where(r => r.DepartmentId == departmentId && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase))));

		public async Task<AcademyGuidanceState> UpsertAsync(AcademyGuidanceState state, CancellationToken cancellationToken = default)
		{
			var existing = Rows.FirstOrDefault(r => r.DepartmentId == state.DepartmentId && string.Equals(r.UserId, state.UserId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(r.GuidanceKey, state.GuidanceKey, StringComparison.Ordinal));
			state.AcademyGuidanceStateId = existing?.AcademyGuidanceStateId ?? 0;
			return existing == null ? await InsertAsync(state, cancellationToken) : await UpdateAsync(state, cancellationToken);
		}
	}
}
