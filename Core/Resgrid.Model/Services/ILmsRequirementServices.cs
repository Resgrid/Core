using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T3 (protocols-training-knowledge-plan §13 T3 items 1-6, LMS): learning requirements, the compliance matrix, training-backed
	// enforcement with the blast-radius guard (R-L11), learning paths with prerequisites and onboarding. LMS services consume baseline
	// services (audiences, leave, certifications, learning records); baseline services never call these (§17.3).

	public interface ILmsRequirementService
	{
		/// <summary>The department's requirement settings (setting 143), normalized; the defaults when unset.</summary>
		Task<LearningRequirementSettings> GetSettingsAsync(int departmentId);

		/// <summary>Saves the settings (LMS managers, the add-on active). Audited.</summary>
		Task<LmsResult> SaveSettingsAsync(LmsActor actor, LearningRequirementSettings settings, CancellationToken cancellationToken = default);

		Task<List<LmsRequirementRow>> GetRequirementsAsync(LmsActor actor, DateTime utcNow);

		Task<LmsRequirement> GetRequirementAsync(int departmentId, int lmsRequirementId);

		Task<List<LmsRequirementAudience>> GetAudienceAsync(int departmentId, int lmsRequirementId);

		/// <summary>Creates or updates a requirement (LMS managers, the add-on active), then applies it to its audience at once.</summary>
		Task<LmsResult> SaveRequirementAsync(LmsActor actor, LmsRequirementInput input, CancellationToken cancellationToken = default);

		/// <summary>Soft-deletes a requirement; its open enforcement actions are undone.</summary>
		Task<LmsResult> DeleteRequirementAsync(LmsActor actor, int lmsRequirementId, CancellationToken cancellationToken = default);

		/// <summary>The person × requirement matrix in the viewer's scope (department-wide managers, or a group admin's group).</summary>
		Task<LmsComplianceMatrix> GetMatrixAsync(LmsActor actor, TrainingManagementScope scope, LmsMatrixFilter filter, DateTime utcNow);

		/// <summary>The department-wide matrix for the LMS report catalog ("compliance-matrix"); callers are already authorized and gated.</summary>
		Task<LmsComplianceMatrix> GetDepartmentMatrixAsync(int departmentId, LmsMatrixFilter filter, DateTime utcNow);

		/// <summary>The member's own requirements with their state (My Training, v4). Empty when the department has none or the LMS is off.</summary>
		Task<List<LmsMyRequirement>> GetMyRequirementsAsync(int departmentId, string userId, DateTime utcNow);

		/// <summary>Exempts a member (reason, optional end) or lifts the exemption (R-C2). Audited.</summary>
		Task<LmsResult> SetExemptionAsync(LmsActor actor, TrainingManagementScope scope, int lmsRequirementMemberId, bool exempt, DateTime? untilUtc, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>Extends a member's due date for the current period, or clears the extension (null). Audited.</summary>
		Task<LmsResult> ExtendAsync(LmsActor actor, TrainingManagementScope scope, int lmsRequirementMemberId, DateTime? dueUtc, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>"Who would lose which role today" (R-L11): the next enforcement run, and whether the guard would refuse it.</summary>
		Task<LmsEnforcementPreview> PreviewEnforcementAsync(LmsActor actor, DateTime utcNow);

		Task<List<LmsRequirementEnforcement>> GetEnforcementHistoryAsync(LmsActor actor, int take);

		/// <summary>An officer undoes one enforcement action (the certification and the roles it took come back). Audited.</summary>
		Task<LmsResult> UndoEnforcementAsync(LmsActor actor, int lmsRequirementEnforcementId, CancellationToken cancellationToken = default);

		// ---- worker 93 / worker 83 ----------------------------------------------------------------------------------------------

		/// <summary>
		/// The department's daily pass (worker 93, after it claimed the local day): asks the LMS gate once; reconciles audiences, computes
		/// states (workflow events and notices on transitions), auto-assigns onboarding, undoes enforcement for members now compliant,
		/// enforces under the blast-radius guard and advances department paths. On a lapse it reverts open enforcement (rule 10) only.
		/// </summary>
		Task<LmsRequirementRunResult> RunDailyAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Undoes the open enforcement actions whose member completed (or no longer owes) the requirement. No gate: undoing is always allowed.</summary>
		Task<int> UndoCompletedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Worker 83's hook: members whose group, role, shift, certification or unit changed, or who joined (null = the whole department).
		/// Returns at once (one query) when the department has no live requirement; otherwise asks the gate once and applies the
		/// requirements to those members (new rows, onboarding assignment, notices).
		/// </summary>
		Task<LmsRequirementRunResult> OnAudienceChangesAsync(int departmentId, IReadOnlyCollection<string> userIds, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsPathService
	{
		/// <summary>Seeds the Academy role paths as system paths and links A1's ordered-list assignments to them (idempotent).</summary>
		Task<int> EnsureSystemPathsAsync(CancellationToken cancellationToken = default);

		Task<List<LmsPathRow>> GetPathsAsync(LmsActor actor);

		Task<LmsPathDetail> GetPathAsync(LmsActor actor, int lmsPathId);

		Task<LmsResult> SavePathAsync(LmsActor actor, LmsPathInput input, CancellationToken cancellationToken = default);

		Task<LmsResult> DeletePathAsync(LmsActor actor, int lmsPathId, CancellationToken cancellationToken = default);

		/// <summary>Assigns a path: one assignment per member, the unlocked course steps enrolled now, the rest as they unlock.</summary>
		Task<LmsAssignOutcome> AssignAsync(LmsActor actor, int lmsPathId, IEnumerable<IContentAudienceRule> who, DateTime? dueOnUtc, int? lmsRequirementId = null,
			CancellationToken cancellationToken = default);

		/// <summary>The member's paths with their progress; enrolls steps that unlocked since (department paths need the add-on).</summary>
		Task<List<LmsMyPath>> GetMyPathsAsync(LmsActor actor, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<List<LmsPathProgressRow>> GetProgressAsync(LmsActor actor, int lmsPathId);

		/// <summary>Worker 93: advances the department's open path assignments (enrolls unlocked steps, stamps finished paths, issues certifications).</summary>
		Task<int> AdvanceDepartmentAsync(int departmentId, LmsDepartmentGate gate, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Members who finished each path (from their step completions), as requirement facts (RefId = path id).</summary>
		Task<List<LmsRequirementFact>> GetCompletionFactsAsync(int departmentId, IEnumerable<int> lmsPathIds);

		/// <summary>A path's display title (Academy paths are localized).</summary>
		string TitleOf(LmsPath path, string locale);
	}

	/// <summary>
	/// Direct requirement notifications (EventTypes 61-63): push + email through the communication service with a push code the apps route
	/// on ("LR:{requirementId}") and the web link in the text. Active members only; nothing goes out where the department cannot transmit.
	/// </summary>
	public interface ILmsRequirementNotifier
	{
		Task<int> NotifyAssignedAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, DateTime? dueUtc, CancellationToken cancellationToken = default);

		Task<int> NotifyDueAsync(int departmentId, string userId, LmsRequirement requirement, string targetName, LmsRequirementStates state, DateTime? dueUtc,
			CancellationToken cancellationToken = default);

		Task<int> NotifySupervisorsAsync(int departmentId, string memberUserId, LmsRequirement requirement, string targetName, bool completed,
			CancellationToken cancellationToken = default);

		Task<int> NotifyEnforcementAsync(int departmentId, string userId, LmsRequirement requirement, string certificationName, bool undone,
			CancellationToken cancellationToken = default);

		Task<int> NotifyRefusedAsync(int departmentId, IReadOnlyCollection<LmsRoleBreach> breaches, int maxSharePercent, CancellationToken cancellationToken = default);
	}
}
