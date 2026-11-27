using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Round 7B (permission 135, baseline/free): record that members completed a baseline training they took together, read recordings and
	/// void one (any target). The recorder needs the permission and, as a group admin, may only record for their own group's members; only
	/// active members; never for themselves; the attestation is required, and a note when it is not from a class. The assignment is marked
	/// complete (the quiz noted as waived), the transcript record says "recorded by" (never the member's own), certification credits follow
	/// through the T1 writer. A completion the member already has stands (the first one is kept). Audited; workflow trigger 244.
	/// </summary>
	public interface ITrainingCompletionRecordingService
	{
		Task<TrainingCompletionRecordingOutcome> RecordTrainingAsync(int departmentId, int trainingId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Undoes a recording with a reason (the permission holder for that member): the learning record and its credits are voided through
		/// the T1 void path and the assignment (or LMS enrolment, through <see cref="ITrainingCompletionRecordingTarget"/>) is reopened.
		/// </summary>
		Task<TrainingCompletionRecordingResult> VoidAsync(int departmentId, int trainingCompletionRecordingId, string userId, string reason,
			CancellationToken cancellationToken = default);

		Task<TrainingCompletionRecording> GetAsync(int departmentId, int trainingCompletionRecordingId);

		Task<List<TrainingCompletionRecording>> GetForMemberAsync(int departmentId, string userId);

		Task<List<TrainingCompletionRecording>> GetForTargetAsync(int departmentId, TrainingCompletionRecordingTargets target, int targetId);

		Task<List<TrainingCompletionRecording>> GetForSourceAsync(int departmentId, TrainingCompletionRecordingSources source, string sourceRefId);

		/// <summary>The record page of a baseline training: every active member (assignees first) with their eligibility and recordings.</summary>
		Task<TrainingCompletionRecordingView> GetTrainingRecordingViewAsync(int departmentId, int trainingId, string viewerUserId);

		/// <summary>The recordings behind a list of learning records (transcripts, exports, v4), keyed by recording id.</summary>
		Task<Dictionary<int, TrainingCompletionRecording>> GetForRecordsAsync(int departmentId, IEnumerable<LearningRecord> records);

		/// <summary>
		/// Shared by the LMS path: who the recorder may record for (permission 135, group scope), or a refusal of the whole request. Null
		/// when they hold the right at all.
		/// </summary>
		Task<(TrainingManagementScope Scope, string Refusal)> AuthorizeAsync(int departmentId, TrainingCompletionRecordingRequest request);

		/// <summary>Shared by the LMS path: audits the recording and raises workflow trigger 244.</summary>
		void Announce(TrainingCompletionRecording recording, bool isExternalLearner);

		/// <summary>
		/// Round 9B (the round-7B "no member notice" gap): tells the member a completion was recorded for them (EventTypes 81, push + e-mail, their
		/// notification settings apply; active members only, never an external learner). Shared by the LMS path; a failure never undoes it.
		/// </summary>
		Task NotifyMemberAsync(TrainingCompletionRecording recording, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The other target kinds of a recording (the LMS layer registers Course): the baseline service calls it to void what the recording made,
	/// so baseline code never references LMS types (§17.3).
	/// </summary>
	public interface ITrainingCompletionRecordingTarget
	{
		TrainingCompletionRecordingTargets Kind { get; }

		/// <summary>Voids the learning record (and credits) and reopens what the recording completed; false when nothing could be undone.</summary>
		Task<bool> VoidAsync(TrainingCompletionRecording recording, string userId, string reason, CancellationToken cancellationToken = default);
	}
}
