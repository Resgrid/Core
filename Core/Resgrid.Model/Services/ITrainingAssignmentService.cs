using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Who is assigned a training and when it is due (T0 item 2): static picks, dynamic audience rules re-evaluated on
	/// membership change (worker 83), relative and late-joiner due dates, withdrawal of never-opened assignments, closing
	/// to new assignees, the one simple repeat, self-enrolment, removal, extensions, exemptions and resets. Every write is
	/// per row (T-D14) and skips deleted, disabled and hidden members (inactive-member policy).
	/// </summary>
	public interface ITrainingAssignmentService
	{
		/// <summary>
		/// Assigns members picked by name (and/or the members of the given audience rules, resolved now). Members already
		/// assigned are left alone; a removed completed member is tracked again. Skipped members come back with the reason.
		/// <paramref name="assignScope"/> limits a group admin to their own group's members (null: no limit).
		/// </summary>
		Task<TrainingAssignmentOutcome> AssignAsync(Training training, IEnumerable<string> pickedUserIds, IEnumerable<IContentAudienceRule> rules,
			TrainingAssignmentSources source, TrainingRightScope assignScope, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Stores the training's audience rules (making the assignment dynamic) and assigns everyone they resolve to now.
		/// Rules are normalized; an empty set makes the training static again (existing assignments stay).
		/// </summary>
		Task<TrainingAssignmentOutcome> SaveAudienceAsync(Training training, IEnumerable<IContentAudienceRule> rules, string actingUserId,
			TrainingRightScope assignScope, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<List<TrainingAudience>> GetAudienceAsync(int departmentId, int trainingId);

		/// <summary>Who the rules would add now (resolved minus already assigned), and who would be skipped and why.</summary>
		Task<TrainingAssignmentOutcome> PreviewAudienceAsync(Training training, IEnumerable<IContentAudienceRule> rules);

		/// <summary>
		/// Worker 83, per queued member: assigns the open dynamic trainings whose audience now includes them and withdraws
		/// never-opened audience assignments whose audience no longer does. Returns (added, withdrawn).
		/// </summary>
		Task<Tuple<int, int>> ReevaluateMemberAsync(int departmentId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 83, for a department-wide change (a group, role or shift deleted): every open dynamic training.</summary>
		Task<Tuple<int, int>> ReevaluateDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// The one simple repeat: completed assignments move to the completion history and every current assignee (and,
		/// for a dynamic training, the current audience) is assigned again with a fresh due date. Advances NextRepeatOn.
		/// </summary>
		Task<int> RunRepeatAsync(Training training, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// A material quiz edit (R-T6): completed members' completions move to the history and they are assigned again with a
		/// fresh due date. Returns how many were re-assigned.
		/// </summary>
		Task<int> ReassignCompletedAsync(Training training, string actingUserId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The member enrols themselves in an open self-enrol training. Null when they may not.</summary>
		Task<TrainingUser> SelfEnrollAsync(int departmentId, int trainingId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes an assignee (T-D15). One who completed is kept as a removed row with the completion; anyone else's row is
		/// deleted. Audited.
		/// </summary>
		Task<bool> RemoveAssigneeAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>Extension (R-C2): a new due date (department-local day), the reason and the approver. Audited.</summary>
		Task<TrainingUser> ExtendAsync(int departmentId, int trainingId, string userId, DateTime newDueLocalDate, string reason, string actingUserId,
			CancellationToken cancellationToken = default);

		/// <summary>Exemption (R-C2): reason, approver and optional expiry (department-local day). Shown as exempt, never complete.</summary>
		Task<TrainingUser> ExemptAsync(int departmentId, int trainingId, string userId, string reason, DateTime? untilLocalDate, string actingUserId,
			CancellationToken cancellationToken = default);

		Task<TrainingUser> ClearExemptionAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The training's due rule changed (fixed date or relative days): every open assignment that was not extended gets the
		/// due moment the new rule gives it (from when it was assigned), with its reminders re-armed. Returns how many moved.
		/// </summary>
		Task<int> RescheduleOpenAssignmentsAsync(Training training, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Resets a member: any completion moves to the history, progress is cleared, a fresh due date is set.</summary>
		Task<TrainingUser> ResetMemberAsync(int departmentId, int trainingId, string userId, string actingUserId, CancellationToken cancellationToken = default);
	}
}
