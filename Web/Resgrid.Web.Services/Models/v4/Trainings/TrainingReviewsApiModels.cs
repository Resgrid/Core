using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Trainings
{
	// Round 9B: training hours submissions with evidence and their optional review, for Omni (a reviewer's inbox and decisions, a member's own
	// submissions and resubmission with the camera). All times are UTC.

	/// <summary>One evidence file of a submission (no bytes: GetEvidenceFile returns them).</summary>
	public class TrainingEvidenceFileData
	{
		public int FileId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long SizeBytes { get; set; }
		/// <summary>TrainingEvidenceKinds name: Other, Certificate, Photo, Document or Receipt.</summary>
		public string Kind { get; set; }
		/// <summary>True for a JPEG, PNG, GIF or WebP image or a PDF (shown inline); other types download.</summary>
		public bool Previewable { get; set; }
		public DateTime UploadedOn { get; set; }
	}

	/// <summary>One step of a submission's review.</summary>
	public class TrainingSubmissionEventData
	{
		/// <summary>TrainingSubmissionEventKinds name: Submitted, SelfReported, Approved, Denied, InfoRequested or Resubmitted.</summary>
		public string Step { get; set; }
		public string ActorUserId { get; set; }
		public string Note { get; set; }
		public decimal? Hours { get; set; }
		public int FileCount { get; set; }
		public DateTime On { get; set; }
	}

	/// <summary>A submission waiting in a reviewer's inbox.</summary>
	public class TrainingReviewItemData
	{
		public LearningRecordData Record { get; set; }
		/// <summary>The member who submitted it.</summary>
		public string UserId { get; set; }
		public string MemberName { get; set; }
		public string Notes { get; set; }
		public int EvidenceFiles { get; set; }
		/// <summary>When it was (re)submitted (UTC): the inbox is oldest first.</summary>
		public DateTime WaitingSince { get; set; }
	}

	public class TrainingReviewInboxResult : StandardApiResponseV4Base
	{
		public List<TrainingReviewItemData> Data { get; set; } = new List<TrainingReviewItemData>();
		/// <summary>Submissions waiting for the caller's review (any filter).</summary>
		public int AwaitingCount { get; set; }
		/// <summary>Submissions waiting for their member's answer.</summary>
		public int InfoRequestedCount { get; set; }
		public DateTime? OldestWaitingSince { get; set; }
		/// <summary>False when the caller reviews nobody's submissions.</summary>
		public bool IsReviewer { get; set; }
	}

	/// <summary>A submission with its evidence and history, for its member or a reviewer.</summary>
	public class TrainingSubmissionData
	{
		public LearningRecordData Record { get; set; }
		public string UserId { get; set; }
		public string MemberName { get; set; }
		public string Notes { get; set; }
		public string Location { get; set; }
		public bool IsOwner { get; set; }
		/// <summary>The caller may approve, deny or ask for more information.</summary>
		public bool CanReview { get; set; }
		/// <summary>The member may add evidence or corrections and resubmit (pending).</summary>
		public bool CanRespond { get; set; }
		public List<TrainingEvidenceFileData> Files { get; set; } = new List<TrainingEvidenceFileData>();
		public List<TrainingSubmissionEventData> History { get; set; } = new List<TrainingSubmissionEventData>();
	}

	public class TrainingSubmissionResult : StandardApiResponseV4Base
	{
		public TrainingSubmissionData Data { get; set; }
		/// <summary>Why an action was refused (a learning_record_* code), or null.</summary>
		public string Error { get; set; }
	}

	public class TrainingSubmissionsResult : StandardApiResponseV4Base
	{
		public List<LearningRecordData> Data { get; set; } = new List<LearningRecordData>();
	}

	public class ReviewTrainingSubmissionInput
	{
		public int Id { get; set; }
		/// <summary>Approve: an optional note. Deny: the reason (required). More information: what is needed (required).</summary>
		public string Note { get; set; }
		/// <summary>Approve only: corrected hours (optional).</summary>
		public decimal? Hours { get; set; }
	}

	public class BulkApproveTrainingSubmissionsInput
	{
		/// <summary>At most 100 submissions.</summary>
		public List<int> Ids { get; set; } = new List<int>();
		public string Note { get; set; }
	}

	public class TrainingBulkReviewResultData
	{
		public List<int> Approved { get; set; } = new List<int>();
		/// <summary>Submission id to the refusal code.</summary>
		public Dictionary<int, string> Refused { get; set; } = new Dictionary<int, string>();
	}

	public class TrainingBulkReviewApiResult : StandardApiResponseV4Base
	{
		public TrainingBulkReviewResultData Data { get; set; } = new TrainingBulkReviewResultData();
	}

	public class RespondToTrainingSubmissionInput
	{
		public int Id { get; set; }
		/// <summary>The member's answer to the reviewer.</summary>
		public string Note { get; set; }
		/// <summary>Corrected hours (optional).</summary>
		public decimal? Hours { get; set; }
		public decimal? CeHours { get; set; }
		/// <summary>Corrected category (optional; 0 removes it).</summary>
		public int? CategoryId { get; set; }
		/// <summary>More evidence (camera photos, scans): at most 5 files, 10 MB each, 30 MB together, base64.</summary>
		public List<TrainingEvidenceFileInput> Files { get; set; }
	}
}
