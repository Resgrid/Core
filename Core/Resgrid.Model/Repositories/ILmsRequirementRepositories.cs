using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T3 (LMS): learning requirements, their members, training-backed enforcement, worker 93's clock and learning paths
	// (M0364-M0366). LMS tables reference baseline rows by id only (§17.3).

	public interface ILmsRequirementsRepository : IRepository<LmsRequirement>
	{
		Task<List<LmsRequirement>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		Task<LmsRequirement> GetByIdForDepartmentAsync(int departmentId, int lmsRequirementId);

		/// <summary>True when the department has a live (active, not deleted) requirement: the cheap check before any gate call.</summary>
		Task<bool> HasLiveAsync(int departmentId);

		/// <summary>
		/// Worker 93's due-work query (R-L9): departments with a live requirement, an open enforcement action, or an open department path
		/// assignment. Only these are ever asked the LMS gate, once per department per local day.
		/// </summary>
		Task<List<int>> GetDepartmentIdsWithDueWorkAsync();
	}

	public interface ILmsRequirementAudiencesRepository : IRepository<LmsRequirementAudience>
	{
		Task<List<LmsRequirementAudience>> GetForDepartmentAsync(int departmentId);

		Task ReplaceAsync(int departmentId, int lmsRequirementId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default);
	}

	public interface ILmsRequirementMembersRepository : IRepository<LmsRequirementMember>
	{
		/// <summary>The department's rows that have not left their audience.</summary>
		Task<List<LmsRequirementMember>> GetOpenForDepartmentAsync(int departmentId);

		Task<List<LmsRequirementMember>> GetOpenForUserAsync(int departmentId, string userId);

		Task<LmsRequirementMember> GetByIdForDepartmentAsync(int departmentId, int lmsRequirementMemberId);

		/// <summary>Inserts a member's open row; a concurrent twin (the filtered unique index) returns the existing row and false.</summary>
		Task<(LmsRequirementMember Member, bool Created)> InsertOpenAsync(LmsRequirementMember member, CancellationToken cancellationToken = default);

		Task<int> MarkLeftAsync(int departmentId, IEnumerable<int> lmsRequirementMemberIds, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Writes the computed state columns (State, StateChangedOn, DueOn, LastCompletedOn).</summary>
		Task<int> SaveStateAsync(LmsRequirementMember member, CancellationToken cancellationToken = default);

		/// <summary>Writes the exemption and extension columns (R-C2).</summary>
		Task<int> SaveExemptionAsync(LmsRequirementMember member, CancellationToken cancellationToken = default);

		/// <summary>Claims the at-risk (or overdue) notice for one due moment before it is sent: false when it was already claimed.</summary>
		Task<bool> TryClaimNoticeAsync(int departmentId, int lmsRequirementMemberId, bool overdue, DateTime dueOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Claims the auto-assignment for one due moment (onboarding and renewals): false when it was already made.</summary>
		Task<bool> TryClaimAssignmentAsync(int departmentId, int lmsRequirementMemberId, DateTime dueOnUtc, CancellationToken cancellationToken = default);
	}

	public interface ILmsRequirementEnforcementsRepository : IRepository<LmsRequirementEnforcement>
	{
		Task<List<LmsRequirementEnforcement>> GetOpenForDepartmentAsync(int departmentId);

		/// <summary>The department's actions, newest first (the enforcement history).</summary>
		Task<List<LmsRequirementEnforcement>> GetRecentForDepartmentAsync(int departmentId, int take);

		/// <summary>Departments with an action not undone yet (the undo pass needs no gate).</summary>
		Task<List<int>> GetDepartmentIdsWithOpenAsync();

		/// <summary>Records an action; false when one is already open for the requirement and member (the filtered unique index).</summary>
		Task<bool> TryInsertOpenAsync(LmsRequirementEnforcement enforcement, CancellationToken cancellationToken = default);

		/// <summary>Marks an action undone once (compare-and-set on UndoneOn).</summary>
		Task<bool> TryMarkUndoneAsync(int departmentId, int lmsRequirementEnforcementId, LmsEnforcementUndoReasons reason, string restoredRolesJson, DateTime utcNow,
			CancellationToken cancellationToken = default);
	}

	public interface ILmsRequirementDepartmentStatesRepository : IRepository<LmsRequirementDepartmentState>
	{
		Task<LmsRequirementDepartmentState> GetForDepartmentAsync(int departmentId);

		/// <summary>Claims the department-local day of the daily pass before it runs: false when it was already claimed.</summary>
		Task<bool> TryClaimDayAsync(int departmentId, DateTime localDate, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Gives the day back after a failed pass so the next tick retries it.</summary>
		Task ReleaseDayAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default);

		Task StampRefusedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsPathsRepository : IRepository<LmsPath>
	{
		/// <summary>The department's paths and the system (Academy) paths, not deleted.</summary>
		Task<List<LmsPath>> GetVisibleAsync(int departmentId);

		Task<LmsPath> GetVisibleByIdAsync(int departmentId, int lmsPathId);

		Task<LmsPath> GetBySystemKeyAsync(string systemKey);
	}

	public interface ILmsPathStepsRepository : IRepository<LmsPathStep>
	{
		Task<List<LmsPathStep>> GetForPathsAsync(IEnumerable<int> lmsPathIds);

		/// <summary>Replaces a path's steps (deletes then inserts, in the caller's transaction when there is one).</summary>
		Task ReplaceAsync(int departmentId, int lmsPathId, IEnumerable<LmsPathStep> steps, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Read-only projections the requirement engine counts (never whole rows, never attachment bytes): course completions (LMS enrollments,
	/// Academy included), baseline training completions and category hours (the T1 ledger), passed skill evaluations (the ledger's
	/// SkillEvaluation kind, T4's source).
	/// </summary>
	public interface ILmsRequirementFactsRepository
	{
		Task<List<LmsRequirementFact>> GetCourseCompletionsAsync(int departmentId, IEnumerable<int> lmsCourseIds);

		/// <summary>
		/// Round 9A: as <see cref="GetCourseCompletionsAsync"/>, with the learning record of each completed enrolment found in the ledger when
		/// the enrolment does not name it yet (the certification engine runs while the completion is being written, before the enrolment is
		/// stamped with its record).
		/// </summary>
		Task<List<LmsRequirementFact>> GetCourseCompletionsWithRecordsAsync(int departmentId, IEnumerable<int> lmsCourseIds) => GetCourseCompletionsAsync(departmentId, lmsCourseIds);

		/// <summary>Verified baseline training completions (RefId = TrainingId) of the department.</summary>
		Task<List<LmsRequirementFact>> GetTrainingCompletionsAsync(int departmentId, IEnumerable<int> trainingIds);

		/// <summary>Verified records of the categories completed at or after <paramref name="fromUtc"/> (Amount = hours).</summary>
		Task<List<LmsRequirementFact>> GetCategoryHoursAsync(int departmentId, IEnumerable<int> categoryIds, DateTime fromUtc);

		/// <summary>Verified SkillEvaluation records whose course code is one of <paramref name="codes"/> (Code = the course code).</summary>
		Task<List<LmsRequirementFact>> GetEvaluationsAsync(int departmentId, IEnumerable<string> codes);
	}
}
