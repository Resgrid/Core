using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T3 part 2 (round 4C, M0367–M0370). Every read is department scoped; no list query loads more than its page.

	public interface ILmsTrainingPlansRepository : IRepository<LmsTrainingPlan>
	{
		Task<List<LmsTrainingPlan>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);
		Task<LmsTrainingPlan> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingPlanId);
	}

	public interface ILmsTrainingPlanItemsRepository : IRepository<LmsTrainingPlanItem>
	{
		Task<List<LmsTrainingPlanItem>> GetForPlanAsync(int departmentId, int lmsTrainingPlanId);
		Task<LmsTrainingPlanItem> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingPlanItemId);
	}

	public interface ILmsCompetencyFrameworksRepository : IRepository<LmsCompetencyFramework>
	{
		Task<List<LmsCompetencyFramework>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);
		Task<LmsCompetencyFramework> GetByIdForDepartmentAsync(int departmentId, int lmsCompetencyFrameworkId);
		/// <summary>The live framework imported from this CASE document, if any.</summary>
		Task<LmsCompetencyFramework> GetByCaseDocumentIdAsync(int departmentId, string caseDocumentId);
	}

	public interface ILmsCompetencyItemsRepository : IRepository<LmsCompetencyItem>
	{
		Task<List<LmsCompetencyItem>> GetForFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId, bool includeDeleted = false);
		Task<LmsCompetencyItem> GetByIdForDepartmentAsync(int departmentId, int lmsCompetencyItemId);
		/// <summary>Sets the parent of an item (imports insert items first, then link them).</summary>
		Task SetParentAsync(int departmentId, int lmsCompetencyItemId, int? parentItemId, CancellationToken cancellationToken = default);
	}

	public interface ILmsCompetencyAlignmentsRepository : IRepository<LmsCompetencyAlignment>
	{
		Task<List<LmsCompetencyAlignment>> GetForDepartmentAsync(int departmentId);
		Task<List<LmsCompetencyAlignment>> GetForItemsAsync(int departmentId, IEnumerable<int> lmsCompetencyItemIds);
		Task<List<LmsCompetencyAlignment>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId);
		/// <summary>Tags an item on a subject unless it already is (the unique index decides a race); true when a row was written.</summary>
		Task<bool> AddAsync(LmsCompetencyAlignment alignment, CancellationToken cancellationToken = default);
		Task<int> RemoveAsync(int departmentId, int lmsCompetencyAlignmentId, CancellationToken cancellationToken = default);
	}

	public interface ILmsTrainingRequestsRepository : IRepository<LmsTrainingRequest>
	{
		Task<LmsTrainingRequest> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingRequestId);
		Task<List<LmsTrainingRequest>> GetForUserAsync(int departmentId, string userId);
		/// <summary>Requests submitted in [from, to) (UTC), newest first, at most <paramref name="take"/>.</summary>
		Task<List<LmsTrainingRequest>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int take = 2000);
		/// <summary>Requests waiting for a decision.</summary>
		Task<List<LmsTrainingRequest>> GetOpenAsync(int departmentId);
		/// <summary>
		/// Writes the request's state columns only when it is still in <paramref name="expectedStatus"/> at
		/// <paramref name="expectedStep"/> (two approvers deciding at once: one wins); true when this call changed it.
		/// </summary>
		Task<bool> TryTransitionAsync(LmsTrainingRequest request, int expectedStatus, int? expectedStep, CancellationToken cancellationToken = default);
	}

	public interface ILmsTrainingRequestDecisionsRepository : IRepository<LmsTrainingRequestDecision>
	{
		Task<List<LmsTrainingRequestDecision>> GetForRequestAsync(int departmentId, int lmsTrainingRequestId);
	}

	public interface ILmsReportFoldersRepository : IRepository<LmsReportFolder>
	{
		Task<List<LmsReportFolder>> GetForDepartmentAsync(int departmentId);
		Task<LmsReportFolder> GetByIdForDepartmentAsync(int departmentId, int lmsReportFolderId);
	}

	public interface ILmsReportDefinitionsRepository : IRepository<LmsReportDefinition>
	{
		Task<List<LmsReportDefinition>> GetForDepartmentAsync(int departmentId);
		Task<LmsReportDefinition> GetByIdForDepartmentAsync(int departmentId, int lmsReportDefinitionId);
		/// <summary>The due-work query of worker 94: scheduled, live definitions whose next run is at or before <paramref name="utcNow"/>.</summary>
		Task<List<LmsReportDefinition>> GetDueAsync(DateTime utcNow, int take);
		/// <summary>
		/// Claims one due run: moves NextRunUtc from <paramref name="expectedNextRunUtc"/> to <paramref name="nextRunUtc"/> and stamps
		/// the run; false when another worker claimed it first or the definition changed.
		/// </summary>
		Task<bool> TryClaimRunAsync(int lmsReportDefinitionId, DateTime expectedNextRunUtc, DateTime? nextRunUtc, DateTime ranOnUtc, CancellationToken cancellationToken = default);
		Task SetLastRunStatusAsync(int lmsReportDefinitionId, int status, CancellationToken cancellationToken = default);
		/// <summary>Moves every definition of a deleted folder to no folder.</summary>
		Task ClearFolderAsync(int departmentId, int lmsReportFolderId, CancellationToken cancellationToken = default);
	}

	public interface ILmsReportRunsRepository : IRepository<LmsReportRun>
	{
		Task<List<LmsReportRun>> GetForDefinitionAsync(int departmentId, int lmsReportDefinitionId, int take);
	}

	public interface ILmsCertificationCycleRulesRepository : IRepository<LmsCertificationCycleRule>
	{
		Task<List<LmsCertificationCycleRule>> GetForDepartmentAsync(int departmentId);
		/// <summary>The due-work query of worker 94's cycle sweep: departments that have at least one rule.</summary>
		Task<List<int>> GetDepartmentIdsAsync();
	}

	public interface ILmsCertificationAlertsRepository : IRepository<LmsCertificationAlert>
	{
		/// <summary>Inserts the claim unless one exists for (record, kind, key); true when this call claimed it.</summary>
		Task<bool> TryClaimAsync(LmsCertificationAlert alert, CancellationToken cancellationToken = default);
		Task SetRecipientsAsync(int lmsCertificationAlertId, int recipients, CancellationToken cancellationToken = default);
	}

	public interface ILmsProgramStatesRepository : IRepository<LmsProgramState>
	{
		Task<LmsProgramState> GetForDepartmentAsync(int departmentId);
		/// <summary>Claims the department-local day of the cycle sweep; false when that day was already claimed.</summary>
		Task<bool> TryClaimCycleSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default);
		Task ReleaseCycleSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default);
		/// <summary>Round 9A: claims the department-local day of the requirement-set sweep; false when that day was already claimed.</summary>
		Task<bool> TryClaimRequirementSetSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default);
		Task ReleaseRequirementSetSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default);
		Task SetFamiliarizationScannedThroughAsync(int departmentId, DateTime scannedThroughUtc, CancellationToken cancellationToken = default);
		/// <summary>Departments that saved program settings (the familiarization due-work query reads only these).</summary>
		Task<List<int>> GetDepartmentsWithProgramSettingsAsync();
	}

	/// <summary>Read-only views of Records visits for building-familiarization credit (explicit columns; nothing protected is read).</summary>
	public interface ILmsFamiliarizationSourceRepository
	{
		/// <summary>Occupancy inspections changed in (since, until], oldest change first.</summary>
		Task<List<LmsFamiliarizationVisit>> GetInspectionsChangedAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take);
		/// <summary>Occupancy pre-plan reviews recorded in (since, until].</summary>
		Task<List<LmsFamiliarizationVisit>> GetOccupancyReviewsAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take);
		/// <summary>Contact pre-plan reviews recorded in (since, until].</summary>
		Task<List<LmsFamiliarizationVisit>> GetPreplanReviewsAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take);
	}
}
