using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T3 part 2 (round 4C): LMS add-on services. Their callers check the LMS gate (ILmsGateService / ILmsAccessService) first;
	// the worker entry points (RunDueAsync) query for due work first and ask the gate once per department with due work (R-L9).
	// Refusals are InvalidOperationException("lms_program_*") codes the pages show as text.

	/// <summary>
	/// Direct notifications of the T3 program features (training request decisions, certification alerts). Only active members are
	/// addressed (inactive-member-automation-policy); each text is built in the recipient's own language; one failing channel never
	/// stops the others. Returns the members notified.
	/// </summary>
	public interface ILmsProgramNotifier
	{
		Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string titleKey, string messageKey, Func<CultureInfo, object[]> args, string eventCode,
			CancellationToken cancellationToken = default);
	}

	/// <summary>Trainings → Programs → Settings (setting 144).</summary>
	public interface ILmsProgramSettingsService
	{
		Task<LmsProgramSettings> GetAsync(int departmentId);
		Task<LmsProgramSettings> SaveAsync(int departmentId, LmsProgramSettings settings, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>The annual training plan (§13 T3 item 3a, R-C5).</summary>
	public interface ILmsTrainingPlanService
	{
		Task<List<LmsTrainingPlan>> GetPlansAsync(int departmentId);
		Task<LmsTrainingPlan> GetPlanAsync(int departmentId, int lmsTrainingPlanId);
		Task<List<LmsTrainingPlanItem>> GetItemsAsync(int departmentId, int lmsTrainingPlanId);
		Task<LmsTrainingPlan> SavePlanAsync(LmsTrainingPlan plan, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeletePlanAsync(int departmentId, int lmsTrainingPlanId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Adopts a draft (the written plan) or archives an adopted one.</summary>
		Task<LmsTrainingPlan> SetStatusAsync(int departmentId, int lmsTrainingPlanId, LmsTrainingPlanStatuses status, string userId, CancellationToken cancellationToken = default);
		/// <summary>Copies a plan's lines into a new draft for another year (dates move by whole years; calendar links are not copied).</summary>
		Task<LmsTrainingPlan> CopyPlanAsync(int departmentId, int lmsTrainingPlanId, int year, string userId, CancellationToken cancellationToken = default);
		Task<LmsTrainingPlanItem> SaveItemAsync(LmsTrainingPlanItem item, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Planned against actual for the plan's audience, and the forecast against the category targets, as of a time.</summary>
		Task<LmsTrainingPlanDashboard> GetDashboardAsync(int departmentId, int lmsTrainingPlanId, DateTime utcNow);
		/// <summary>
		/// "Make it a calendar item": creates a calendar item for a planned line (a training-type calendar type of the line's category when
		/// the department has one, so check-in hours reach the transcript) and links it. Class sessions are T4; the line keeps a seam.
		/// </summary>
		Task<LmsTrainingPlanItem> CreateCalendarItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>Competency frameworks, tagging and the per-member gap report (§13 T3 item 3b, R-C7).</summary>
	public interface ILmsCompetencyService
	{
		IReadOnlyList<LmsCompetencyCatalogFramework> GetCatalog();
		Task<List<LmsCompetencyFramework>> GetFrameworksAsync(int departmentId);
		Task<LmsCompetencyFramework> GetFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId);
		Task<List<LmsCompetencyItem>> GetItemsAsync(int departmentId, int lmsCompetencyFrameworkId);
		/// <summary>Applies a catalog framework (structure and identifiers only); an applied one is returned again rather than duplicated.</summary>
		Task<LmsCompetencyFramework> ApplyCatalogAsync(int departmentId, string catalogKey, string userId, CancellationToken cancellationToken = default);
		/// <summary>Imports a CASE 1.1 package (CFDocument, CFItems, isChildOf associations). Re-importing the same document updates it.</summary>
		Task<LmsCaseImportResult> ImportCaseAsync(int departmentId, byte[] json, string userId, CancellationToken cancellationToken = default);
		Task<LmsCompetencyFramework> SaveFrameworkAsync(LmsCompetencyFramework framework, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId, string userId, CancellationToken cancellationToken = default);
		Task<LmsCompetencyItem> SaveItemAsync(LmsCompetencyItem item, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteItemAsync(int departmentId, int lmsCompetencyItemId, string userId, CancellationToken cancellationToken = default);
		Task<List<LmsCompetencyAlignment>> GetAlignmentsAsync(int departmentId, int lmsCompetencyFrameworkId);
		Task<List<LmsCompetencyAlignment>> GetAlignmentsForSubjectAsync(int departmentId, LmsCompetencySubjectKinds kind, string subjectId);
		Task<LmsCompetencyAlignment> TagAsync(int departmentId, int lmsCompetencyItemId, LmsCompetencySubjectKinds kind, string subjectId, string userId,
			CancellationToken cancellationToken = default);
		Task<bool> UntagAsync(int departmentId, int lmsCompetencyAlignmentId, string userId, CancellationToken cancellationToken = default);
		/// <summary>The member's gap list for a framework: evidence (and how old it is) per item, or none.</summary>
		Task<LmsCompetencyGapReport> GetMemberGapsAsync(int departmentId, int lmsCompetencyFrameworkId, string userId, DateTime utcNow);
	}

	/// <summary>Training requests with cost and approval (§13a.2), becoming an external submission (T1) on completion.</summary>
	public interface ILmsTrainingRequestService
	{
		Task<LmsTrainingRequest> SubmitAsync(int departmentId, string userId, LmsTrainingRequest request, CancellationToken cancellationToken = default);
		Task<LmsTrainingRequest> WithdrawAsync(int departmentId, int lmsTrainingRequestId, string userId, CancellationToken cancellationToken = default);
		/// <summary>The approver of the current step decides; approving the last step approves the request.</summary>
		Task<LmsTrainingRequest> DecideAsync(int departmentId, int lmsTrainingRequestId, string approverUserId, bool approve, string note,
			CancellationToken cancellationToken = default);
		/// <summary>A training officer cancels an approved request that will not be taken.</summary>
		Task<LmsTrainingRequest> CancelAsync(int departmentId, int lmsTrainingRequestId, string userId, string note, CancellationToken cancellationToken = default);
		/// <summary>The member took the class: writes the external submission (Pending until an officer verifies it, T1).</summary>
		Task<LmsTrainingRequest> CompleteAsync(int departmentId, int lmsTrainingRequestId, string userId, DateTime completedOn, decimal hours, decimal? actualCost,
			string fileName, string contentType, byte[] evidence, CancellationToken cancellationToken = default);
		Task<LmsTrainingRequestView> GetAsync(int departmentId, int lmsTrainingRequestId, string viewerUserId);
		Task<List<LmsTrainingRequest>> GetMineAsync(int departmentId, string userId);
		/// <summary>Open requests the viewer may decide now.</summary>
		Task<List<LmsTrainingRequest>> GetAwaitingDecisionAsync(int departmentId, string approverUserId);
		/// <summary>Every request submitted in the period (training officers).</summary>
		Task<List<LmsTrainingRequest>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);
		Task<LmsTrainingRequestCostSummary> GetCostSummaryAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, LmsTrainingRequestCostGroupings groupBy);
		/// <summary>Whether the person may decide a step of the member's request (excludes the member).</summary>
		Task<bool> CanDecideAsync(int departmentId, string approverUserId, string requesterUserId, LmsTrainingRequestSteps step);
	}

	/// <summary>
	/// One report of the LMS report catalog (§13a.3). Implementations register with the container; another stream adds its own (4B's
	/// compliance matrix registers as key "compliance-matrix") without touching the catalog.
	/// </summary>
	public interface ILmsReportSource
	{
		LmsReportDescriptor Descriptor { get; }
		Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text);
	}

	/// <summary>The report catalog, saved reports in folders, and scheduled delivery on worker 94.</summary>
	public interface ILmsReportCatalogService
	{
		IReadOnlyList<LmsReportDescriptor> GetCatalog();
		LmsReportDescriptor Find(string reportKey);
		/// <summary>Builds a report for <paramref name="localToday"/> in <paramref name="culture"/>.</summary>
		Task<LmsReportTable> RunAsync(int departmentId, string userId, string reportKey, LmsReportParameters parameters, CultureInfo culture, DateTime utcNow);
		byte[] ToCsvBytes(LmsReportTable table);
		byte[] ToPdfBytes(LmsReportTable table, string departmentName, CultureInfo culture);

		Task<List<LmsReportFolder>> GetFoldersAsync(int departmentId);
		Task<LmsReportFolder> SaveFolderAsync(LmsReportFolder folder, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteFolderAsync(int departmentId, int lmsReportFolderId, string userId, CancellationToken cancellationToken = default);
		Task<List<LmsReportDefinition>> GetDefinitionsAsync(int departmentId);
		Task<LmsReportDefinition> GetDefinitionAsync(int departmentId, int lmsReportDefinitionId);
		Task<LmsReportDefinition> SaveDefinitionAsync(LmsReportDefinition definition, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteDefinitionAsync(int departmentId, int lmsReportDefinitionId, string userId, CancellationToken cancellationToken = default);
		Task<List<LmsReportRun>> GetRunsAsync(int departmentId, int lmsReportDefinitionId);
		/// <summary>Worker 94: sends every due scheduled report (due-work query first, the gate once per department). Returns runs handled.</summary>
		Task<int> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Certification cycle auto-renew and LMS alerts (§13a.1).</summary>
	public interface ILmsCertificationCycleService
	{
		Task<List<LmsCertificationCycleRule>> GetRulesAsync(int departmentId);
		Task<LmsCertificationCycleRule> SaveRuleAsync(LmsCertificationCycleRule rule, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteRuleAsync(int departmentId, int lmsCertificationCycleRuleId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Worker 94: departments with rules, once per department-local day, the gate once per department.</summary>
		Task<LmsCertificationCycleRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>One department's renewals and alerts for its local day (the gate was already asked).</summary>
		Task<LmsCertificationCycleRunResult> RunDepartmentAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default);
	}

	/// <summary>Building-familiarization credit from Records inspections and pre-plan reviews (§13a.1), through ILearningRecordWriter.</summary>
	public interface ILmsFamiliarizationCreditService
	{
		Task<LmsFamiliarizationRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Credits one department's visits changed in (since, until] (the gate was already asked); idempotent per visit.</summary>
		Task<LmsFamiliarizationRunResult> CreditAsync(int departmentId, LmsProgramSettings settings, DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken = default);
	}
}
