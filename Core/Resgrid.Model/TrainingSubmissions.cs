using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	// Training hours submissions (round 9B, product-owner requirements 2026-11-25): a member records continuing education / CPD / training
	// hours they did elsewhere with evidence (certificate, photo, document, receipt). It is T1's external submission (LearningRecordSourceKinds
	// .ExternalSubmission), extended: several evidence files, an optional review the department switches on (all submissions, or selected
	// categories and/or above an hours threshold), a reviewer inbox (department-wide and group-scoped reviewers), "more information needed" with
	// a resubmission, member and reviewer notices. Baseline (free). Persisted enums are append only.

	/// <summary>Where a submission stands in review (LearningRecords.ReviewState; null on records that are not submissions). Append only.</summary>
	public enum TrainingSubmissionReviewStates
	{
		/// <summary>Waiting for a reviewer (Status Pending). A T1 submission written before M0408 reads as this.</summary>
		AwaitingReview = 0,
		/// <summary>A reviewer asked for more information; the member adds evidence or corrects it and resubmits (Status Pending).</summary>
		InfoRequested = 1,
		/// <summary>A reviewer approved it (Status Verified).</summary>
		Approved = 2,
		/// <summary>A reviewer denied it with a reason (Status Rejected).</summary>
		Denied = 3,
		/// <summary>The department reviews nothing (or nothing of this kind): accepted on submit and shown as self-reported (Status Verified).</summary>
		SelfReported = 4
	}

	/// <summary>Whether member submissions need a reviewer (TrainingRecordSettings.SubmissionReviewMode). Append only.</summary>
	public enum TrainingSubmissionReviewModes
	{
		/// <summary>Every submission waits for a reviewer (the T1 behaviour, and the default).</summary>
		Always = 0,
		/// <summary>No review: a submission is accepted on submit and recorded as self-reported.</summary>
		Off = 1,
		/// <summary>Only submissions in the listed categories, or above the hours threshold, wait for a reviewer; the rest are self-reported.</summary>
		Selected = 2
	}

	/// <summary>How reviewers hear about new submissions (TrainingRecordSettings.ReviewerNotices). Append only.</summary>
	public enum TrainingReviewerNoticeModes
	{
		/// <summary>One notice per submission (and resubmission).</summary>
		Immediate = 0,
		/// <summary>One notice a day with the count waiting for them (for departments with many submissions).</summary>
		DailyDigest = 1,
		/// <summary>No notices: reviewers use the inbox and the home dashboard item.</summary>
		None = 2
	}

	/// <summary>What an evidence file is, as the member labels it (LearningRecordFiles.EvidenceKind). Append only.</summary>
	public enum TrainingEvidenceKinds
	{
		Other = 0,
		Certificate = 1,
		Photo = 2,
		Document = 3,
		Receipt = 4
	}

	/// <summary>One step in a submission's review history (LearningRecordReviewEvents.Kind). Append only.</summary>
	public enum TrainingSubmissionEventKinds
	{
		Submitted = 0,
		SelfReported = 1,
		Approved = 2,
		Denied = 3,
		InfoRequested = 4,
		Resubmitted = 5
	}

	/// <summary>How a transcript record came about, for the transcript and exports (derived, never stored).</summary>
	public enum LearningRecordProvenances
	{
		/// <summary>Recorded by the system from its source: a training completed, a class, attendance, an acknowledgment, a course.</summary>
		System = 0,
		/// <summary>The member reported it and nobody reviewed it (accepted on submit, or still waiting).</summary>
		SelfReported = 1,
		/// <summary>A reviewer or officer checked and approved it.</summary>
		Reviewed = 2,
		/// <summary>An instructor recorded the completion for the member (permission 135).</summary>
		RecordedByOther = 3,
		/// <summary>Brought in by a history import or the history backfill.</summary>
		Imported = 4
	}

	/// <summary>Member and reviewer notices of training records (EventTypes 79-81).</summary>
	public enum TrainingRecordNoticeKinds
	{
		/// <summary>79, to the member: the submission arrived and waits for a reviewer.</summary>
		SubmissionReceived = 0,
		/// <summary>79, to the member: the submission was accepted on submit (no review) and counts.</summary>
		SubmissionAccepted = 1,
		/// <summary>79, to the member: a reviewer approved it.</summary>
		SubmissionApproved = 2,
		/// <summary>79, to the member: a reviewer denied it (with the reason).</summary>
		SubmissionDenied = 3,
		/// <summary>79, to the member: a reviewer asked for more information.</summary>
		SubmissionInfoRequested = 4,
		/// <summary>80, to a reviewer: a submission (or a resubmission) waits for them.</summary>
		ReviewWaiting = 5,
		/// <summary>80, to a reviewer: the daily count of submissions waiting for them.</summary>
		ReviewDigest = 6,
		/// <summary>81, to the member: an instructor recorded a completion for them.</summary>
		CompletionRecorded = 7,
		/// <summary>81, to the member: a recorded completion was voided.</summary>
		CompletionVoided = 8
	}

	/// <summary>
	/// One step of a submission's review (M0409): submitted, self-reported, approved, denied, more information asked for, resubmitted. The
	/// note is the member's or the reviewer's words; the record keeps only the latest. No foreign key (the ledger's rule); purged with the
	/// department.
	/// </summary>
	[Table("LearningRecordReviewEvents")]
	public class LearningRecordReviewEvent : IEntity
	{
		public const int MaxNoteLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LearningRecordReviewEventId { get; set; }

		public int DepartmentId { get; set; }

		public int LearningRecordId { get; set; }

		/// <summary>The member the submission belongs to.</summary>
		public string UserId { get; set; }

		/// <summary><see cref="TrainingSubmissionEventKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>Who acted: the member (submitted, resubmitted) or the reviewer.</summary>
		public string ActorUserId { get; set; }

		public string Note { get; set; }

		/// <summary>The hours the step left on the record (an approval may correct them).</summary>
		public decimal? Hours { get; set; }

		/// <summary>Evidence files added by this step.</summary>
		public int FileCount { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LearningRecordReviewEventId; }
			set { LearningRecordReviewEventId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LearningRecordReviewEvents";

		[NotMapped]
		public string IdName => "LearningRecordReviewEventId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A department's daily reviewer digest (M0410): one row per department and department-local day, inserted before the notices go out so
	/// two workers never send the same day twice.
	/// </summary>
	[Table("TrainingReviewDigests")]
	public class TrainingReviewDigest : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingReviewDigestId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The department-local day (date only).</summary>
		public DateTime DigestDate { get; set; }

		public int Reviewers { get; set; }

		public int Submissions { get; set; }

		public DateTime SentOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingReviewDigestId; }
			set { TrainingReviewDigestId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingReviewDigests";

		[NotMapped]
		public string IdName => "TrainingReviewDigestId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One evidence file a member uploads with a submission or a resubmission (web form or v4).</summary>
	public class TrainingEvidenceUpload
	{
		public string FileName { get; set; }

		public string ContentType { get; set; }

		public byte[] Data { get; set; }

		/// <summary><see cref="TrainingEvidenceKinds"/>.</summary>
		public int Kind { get; set; }
	}

	/// <summary>What a member may correct when they resubmit (null: unchanged).</summary>
	public class TrainingSubmissionCorrection
	{
		public decimal? Hours { get; set; }

		public decimal? CeHours { get; set; }

		/// <summary>The category; 0 removes it, null leaves it.</summary>
		public int? CategoryId { get; set; }
	}

	/// <summary>A submission as its member or a reviewer reads it: the record, its evidence (no bytes) and its review history.</summary>
	public class TrainingSubmissionDetail
	{
		public LearningRecord Record { get; set; }

		public List<LearningRecordFile> Files { get; set; } = new List<LearningRecordFile>();

		public List<LearningRecordReviewEvent> Events { get; set; } = new List<LearningRecordReviewEvent>();

		public bool IsOwner { get; set; }

		/// <summary>The viewer may approve, deny or ask for more information (a reviewer of the member, never their own).</summary>
		public bool CanReview { get; set; }
	}

	/// <summary>What one submission or resubmission did.</summary>
	public class TrainingSubmissionOutcome
	{
		public LearningRecord Record { get; set; }

		/// <summary>True when it waits for a reviewer; false when it was accepted as self-reported.</summary>
		public bool ReviewRequired { get; set; }

		/// <summary>A refusal code (learning_record_*), or null.</summary>
		public string Refusal { get; set; }

		public static TrainingSubmissionOutcome Fail(string refusal) => new TrainingSubmissionOutcome { Refusal = refusal };
	}

	/// <summary>The reviewer inbox filters.</summary>
	public class TrainingReviewInboxQuery
	{
		public int DepartmentId { get; set; }

		public string ReviewerUserId { get; set; }

		/// <summary>The awaiting list (default), or the submissions waiting for their member's answer.</summary>
		public bool InfoRequested { get; set; }

		public int? CategoryId { get; set; }

		public string MemberUserId { get; set; }

		/// <summary>Title or course code contains.</summary>
		public string Search { get; set; }

		public int Take { get; set; } = 200;
	}

	/// <summary>The reviewer inbox: submissions the reviewer may decide, oldest first, with counts.</summary>
	public class TrainingReviewInbox
	{
		public List<LearningRecord> Items { get; set; } = new List<LearningRecord>();

		/// <summary>Waiting for this reviewer (any filter).</summary>
		public int AwaitingCount { get; set; }

		/// <summary>Waiting for the member's answer.</summary>
		public int InfoRequestedCount { get; set; }

		/// <summary>When the oldest awaiting submission was (re)submitted (UTC).</summary>
		public DateTime? OldestWaitingSince { get; set; }

		/// <summary>Evidence files per record id.</summary>
		public Dictionary<int, int> FileCounts { get; set; } = new Dictionary<int, int>();

		/// <summary>False when the member reviews nobody's submissions.</summary>
		public bool IsReviewer { get; set; }
	}

	/// <summary>The result of a bulk approval: what was approved and why the rest were not.</summary>
	public class TrainingBulkReviewResult
	{
		public List<int> Approved { get; set; } = new List<int>();

		public Dictionary<int, string> Refused { get; set; } = new Dictionary<int, string>();
	}

	/// <summary>
	/// Whose submissions a member reviews (round 9B). Reviewers are the holders of Record Training Completions (135, the Training Captain /
	/// Instructor right) and T1's record reviewers (Assign Training 126 department-wide, group admins for their group): department admins
	/// and the roles a permission names cover every member, a group admin covers their own group's members. When the department limits
	/// role reviewers to their own group (TrainingRecordSettings.RoleReviewersOwnGroupOnly), a role reviewer covers only the members of the
	/// group they are in. Nobody reviews their own submission.
	/// </summary>
	public class TrainingSubmissionReviewerScope
	{
		public string UserId { get; set; }

		public bool IsDepartmentAdmin { get; set; }

		/// <summary>Reviews every member's submissions.</summary>
		public bool AllMembers { get; set; }

		/// <summary>Reviews these members' submissions (group scope).</summary>
		public HashSet<string> MemberUserIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public bool IsReviewer => AllMembers || (MemberUserIds?.Count ?? 0) > 0;

		public bool Covers(string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(memberUserId) || string.Equals(memberUserId.Trim(), UserId?.Trim(), StringComparison.OrdinalIgnoreCase))
				return false;
			return AllMembers || (MemberUserIds != null && MemberUserIds.Contains(memberUserId.Trim()));
		}

		public static TrainingSubmissionReviewerScope None(string userId) => new TrainingSubmissionReviewerScope { UserId = userId };
	}

	/// <summary>Rules of member hours submissions shared by the services, the web pages and v4 (round 9B).</summary>
	public static class TrainingSubmissionRules
	{
		/// <summary>Evidence files on one submission, all steps together.</summary>
		public const int MaxFiles = 10;

		/// <summary>Evidence files in one upload (a submission or a resubmission).</summary>
		public const int MaxFilesPerUpload = 5;

		/// <summary>One upload's evidence together (each file is capped at <see cref="LearningRecordFile.MaxBytes"/>).</summary>
		public const long MaxUploadBytes = 30L * 1024 * 1024;

		/// <summary>Submissions approved in one bulk action.</summary>
		public const int MaxBulk = 100;

		/// <summary>True for a member's own submission (T1 external submission), whatever its state.</summary>
		public static bool IsSubmission(LearningRecord record) => record != null && record.SourceKind == (int)LearningRecordSourceKinds.ExternalSubmission;

		/// <summary>The submission's review state; a T1 submission written before M0408 has none and reads from its status.</summary>
		public static TrainingSubmissionReviewStates? StateOf(LearningRecord record)
		{
			if (!IsSubmission(record))
				return null;
			if (record.ReviewState.HasValue && Enum.IsDefined(typeof(TrainingSubmissionReviewStates), record.ReviewState.Value))
				return (TrainingSubmissionReviewStates)record.ReviewState.Value;
			switch ((LearningRecordStatuses)record.Status)
			{
				case LearningRecordStatuses.Verified: return string.IsNullOrWhiteSpace(record.VerifiedByUserId) ? TrainingSubmissionReviewStates.SelfReported : TrainingSubmissionReviewStates.Approved;
				case LearningRecordStatuses.Rejected: return TrainingSubmissionReviewStates.Denied;
				case LearningRecordStatuses.Pending: return TrainingSubmissionReviewStates.AwaitingReview;
				default: return null;
			}
		}

		/// <summary>A submission waiting for a reviewer (pending, not waiting for its member).</summary>
		public static bool IsAwaitingReview(LearningRecord record) =>
			IsSubmission(record) && record.Status == (int)LearningRecordStatuses.Pending && StateOf(record) == TrainingSubmissionReviewStates.AwaitingReview;

		/// <summary>A submission waiting for its member's answer.</summary>
		public static bool IsInfoRequested(LearningRecord record) =>
			IsSubmission(record) && record.Status == (int)LearningRecordStatuses.Pending && StateOf(record) == TrainingSubmissionReviewStates.InfoRequested;

		/// <summary>When the submission was last handed to reviewers (UTC): the resubmission, or the submission.</summary>
		public static DateTime WaitingSince(LearningRecord record) => record?.SubmittedOn ?? record?.CreatedOn ?? DateTime.MinValue;

		/// <summary>
		/// Whether a new submission waits for a reviewer under the department's settings: always (the default), never, or only in the listed
		/// categories or above the hours threshold. "Selected" with nothing selected reviews everything (fails safe).
		/// </summary>
		public static bool RequiresReview(TrainingRecordSettings settings, int? categoryId, decimal hours)
		{
			var s = settings ?? new TrainingRecordSettings();
			switch ((TrainingSubmissionReviewModes)s.SubmissionReviewMode)
			{
				case TrainingSubmissionReviewModes.Off:
					return false;
				case TrainingSubmissionReviewModes.Selected:
					var categories = s.ReviewCategoryIds ?? new List<int>();
					var threshold = s.ReviewAboveHours;
					if (categories.Count == 0 && threshold <= 0)
						return true;
					return (categoryId.HasValue && categories.Contains(categoryId.Value)) || (threshold > 0 && hours > threshold);
				default:
					return true;
			}
		}

		/// <summary>How a record came about, for the transcript and its exports.</summary>
		public static LearningRecordProvenances ProvenanceOf(LearningRecord record)
		{
			if (record == null)
				return LearningRecordProvenances.System;
			if (record.IsRecordedByOther)
				return LearningRecordProvenances.RecordedByOther;
			if (record.IsBackfill || record.SourceKind == (int)LearningRecordSourceKinds.Import)
				return LearningRecordProvenances.Imported;
			if (IsSubmission(record))
				return record.Status == (int)LearningRecordStatuses.Verified && !string.IsNullOrWhiteSpace(record.VerifiedByUserId)
					? LearningRecordProvenances.Reviewed : LearningRecordProvenances.SelfReported;
			if (record.Status == (int)LearningRecordStatuses.Verified && !string.IsNullOrWhiteSpace(record.VerifiedByUserId))
				return LearningRecordProvenances.Reviewed;
			return LearningRecordProvenances.System;
		}

		/// <summary>The evidence kind a file is labelled with, or what its type suggests when the member did not say.</summary>
		public static TrainingEvidenceKinds KindOf(int? requested, string contentType)
		{
			if (requested.HasValue && requested.Value > 0 && Enum.IsDefined(typeof(TrainingEvidenceKinds), requested.Value))
				return (TrainingEvidenceKinds)requested.Value;
			if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				return TrainingEvidenceKinds.Photo;
			return TrainingEvidenceKinds.Other;
		}

		/// <summary>Trims a note to <paramref name="max"/>; null when empty.</summary>
		public static string Note(string value, int max = LearningRecordReviewEvent.MaxNoteLength)
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		/// <summary>The state change an event kind stands for, in a submission's history.</summary>
		public static TrainingSubmissionEventKinds EventFor(TrainingSubmissionReviewStates state)
		{
			switch (state)
			{
				case TrainingSubmissionReviewStates.Approved: return TrainingSubmissionEventKinds.Approved;
				case TrainingSubmissionReviewStates.Denied: return TrainingSubmissionEventKinds.Denied;
				case TrainingSubmissionReviewStates.InfoRequested: return TrainingSubmissionEventKinds.InfoRequested;
				case TrainingSubmissionReviewStates.SelfReported: return TrainingSubmissionEventKinds.SelfReported;
				default: return TrainingSubmissionEventKinds.Submitted;
			}
		}
	}
}
