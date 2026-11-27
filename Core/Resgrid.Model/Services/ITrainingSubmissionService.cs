using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Training hours submissions (round 9B, baseline/free): a member records continuing education / CPD / training hours done elsewhere with
	/// evidence files; the department may have them reviewed (all, selected categories, above an hours threshold) by its reviewers
	/// (TrainingSubmissionReviewerScope: Record Training Completions 135 and T1's record reviewers, department-wide or group-scoped). This is
	/// the one entry point for submissions and their review on the web and v4: it writes the T1 ledger through ILearningRecordService, keeps
	/// the review history, and raises the member and reviewer notices (EventTypes 79-80) and workflow triggers 280-284.
	/// </summary>
	public interface ITrainingSubmissionService
	{
		/// <summary>
		/// A member submits hours with evidence. Waits for a reviewer when the department's review settings say so, otherwise it is accepted
		/// at once as self-reported. Refused (Refusal set) when submissions are switched off or the input or a file is not acceptable.
		/// </summary>
		Task<TrainingSubmissionOutcome> SubmitAsync(int departmentId, string userId, LearningRecordDraft draft, IReadOnlyList<TrainingEvidenceUpload> evidence,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The member answers a request for more information (or adds evidence while it waits): more files, a note, corrected hours or category;
		/// the submission goes back to the reviewers.
		/// </summary>
		Task<TrainingSubmissionOutcome> ResubmitAsync(int departmentId, int learningRecordId, string userId, string note, TrainingSubmissionCorrection correction,
			IReadOnlyList<TrainingEvidenceUpload> evidence, CancellationToken cancellationToken = default);

		/// <summary>A reviewer of the member approves (the hours may be corrected); credits follow through the T1 writer.</summary>
		Task<TrainingSubmissionOutcome> ApproveAsync(int departmentId, int learningRecordId, string reviewerUserId, string note, decimal? hours,
			CancellationToken cancellationToken = default);

		/// <summary>A reviewer of the member denies with a reason (required).</summary>
		Task<TrainingSubmissionOutcome> DenyAsync(int departmentId, int learningRecordId, string reviewerUserId, string reason, CancellationToken cancellationToken = default);

		/// <summary>A reviewer of the member asks for more information (the note says what is missing; required).</summary>
		Task<TrainingSubmissionOutcome> RequestInfoAsync(int departmentId, int learningRecordId, string reviewerUserId, string note, CancellationToken cancellationToken = default);

		/// <summary>Approves several submissions at once (at most TrainingSubmissionRules.MaxBulk); each is checked on its own.</summary>
		Task<TrainingBulkReviewResult> BulkApproveAsync(int departmentId, IEnumerable<int> learningRecordIds, string reviewerUserId, string note,
			CancellationToken cancellationToken = default);

		/// <summary>Whose submissions the member reviews.</summary>
		Task<TrainingSubmissionReviewerScope> GetReviewerScopeAsync(int departmentId, string userId);

		/// <summary>The reviewer inbox: the submissions the reviewer may decide (never their own), oldest first, with counts.</summary>
		Task<TrainingReviewInbox> GetInboxAsync(TrainingReviewInboxQuery query);

		/// <summary>Submissions waiting for this reviewer (the dashboard badge); 0 for a member who reviews nobody.</summary>
		Task<int> CountAwaitingAsync(int departmentId, string reviewerUserId);

		/// <summary>A submission for its member or a reviewer of the member (null otherwise): the record, evidence metadata and history.</summary>
		Task<TrainingSubmissionDetail> GetSubmissionAsync(int departmentId, int learningRecordId, string viewerUserId);

		/// <summary>The member's own submissions, newest first.</summary>
		Task<List<LearningRecord>> GetMySubmissionsAsync(int departmentId, string userId);

		/// <summary>An evidence file with its bytes, for its member, a reviewer of the member or a T1 record reviewer; null otherwise.</summary>
		Task<LearningRecordFile> GetEvidenceFileAsync(int departmentId, int learningRecordFileId, string viewerUserId);

		/// <summary>The active members who review this member's submissions (never the member).</summary>
		Task<List<string>> GetReviewerUserIdsAsync(int departmentId, string memberUserId);

		/// <summary>
		/// Worker 85: the daily reviewer digest of the departments that chose it, once per department-local day after the digest hour; only
		/// departments with submissions waiting are read. Returns the notices sent.
		/// </summary>
		Task<int> SendReviewerDigestsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Member and reviewer notices of training records (round 9B; EventTypes 79 TrainingSubmissionNotice, 80 TrainingReviewNotice, 81
	/// TrainingCompletionNotice): push + email through the standard notification path (the member's notification settings decide the
	/// channels), in the member's language, to active members only. The text names the record and the department's reason or request; never
	/// evidence contents.
	/// </summary>
	public interface ITrainingRecordNoticeService
	{
		/// <summary>79: to the submission's member (received, accepted, approved, denied, more information needed).</summary>
		Task<bool> NotifyMemberAsync(LearningRecord record, TrainingRecordNoticeKinds kind, string note, CancellationToken cancellationToken = default);

		/// <summary>80: to each reviewer, one notice per submission (or resubmission). Returns the notices sent.</summary>
		Task<int> NotifyReviewersAsync(LearningRecord record, IEnumerable<string> reviewerUserIds, bool resubmitted, CancellationToken cancellationToken = default);

		/// <summary>80: the daily digest to one reviewer with the count waiting for them.</summary>
		Task<bool> NotifyReviewerDigestAsync(int departmentId, string reviewerUserId, int waiting, CancellationToken cancellationToken = default);

		/// <summary>81: to the member, a completion an instructor recorded for them (or its void).</summary>
		Task<bool> NotifyCompletionAsync(TrainingCompletionRecording recording, bool voided, CancellationToken cancellationToken = default);
	}
}
