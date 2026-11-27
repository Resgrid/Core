using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Training
{
	/// <summary>Round 9B: how submissions and their review show (state pills, provenance, evidence kinds, history steps).</summary>
	public static class TrainingReviewPresentation
	{
		public static string StateKey(TrainingSubmissionReviewStates? state) => state.HasValue ? "State." + state.Value : null;

		public static string StateIcon(TrainingSubmissionReviewStates? state)
		{
			switch (state)
			{
				case TrainingSubmissionReviewStates.Approved: return "fa-check-circle";
				case TrainingSubmissionReviewStates.Denied: return "fa-times-circle";
				case TrainingSubmissionReviewStates.InfoRequested: return "fa-question-circle";
				case TrainingSubmissionReviewStates.SelfReported: return "fa-user";
				default: return "fa-hourglass-half";
			}
		}

		public static string StateClass(TrainingSubmissionReviewStates? state)
		{
			switch (state)
			{
				case TrainingSubmissionReviewStates.Approved: return "label-primary";
				case TrainingSubmissionReviewStates.Denied: return "label-danger";
				case TrainingSubmissionReviewStates.InfoRequested: return "label-info";
				case TrainingSubmissionReviewStates.SelfReported: return "label-default";
				default: return "label-warning";
			}
		}

		public static string ProvenanceKey(LearningRecordProvenances provenance) => "Provenance." + provenance;

		public static string ProvenanceIcon(LearningRecordProvenances provenance)
		{
			switch (provenance)
			{
				case LearningRecordProvenances.SelfReported: return "fa-user";
				case LearningRecordProvenances.Reviewed: return "fa-check-square-o";
				case LearningRecordProvenances.RecordedByOther: return "fa-user-plus";
				case LearningRecordProvenances.Imported: return "fa-file-text-o";
				default: return "fa-cogs";
			}
		}

		public static string EvidenceKindKey(int kind) =>
			"Evidence.Kind." + (Enum.IsDefined(typeof(TrainingEvidenceKinds), kind) ? (TrainingEvidenceKinds)kind : TrainingEvidenceKinds.Other);

		public static string EvidenceIcon(LearningRecordFile file)
		{
			var type = file?.ContentType ?? string.Empty;
			if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				return "fa-file-image-o";
			if (type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
				return "fa-file-pdf-o";
			return "fa-file-text-o";
		}

		public static string StepKey(int kind) =>
			"History.Step." + (Enum.IsDefined(typeof(TrainingSubmissionEventKinds), kind) ? (TrainingSubmissionEventKinds)kind : TrainingSubmissionEventKinds.Submitted);

		public static string Size(long bytes) =>
			bytes >= 1024 * 1024 ? (bytes / 1024d / 1024d).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " MB"
				: Math.Max(1, bytes / 1024).ToString(System.Globalization.CultureInfo.CurrentCulture) + " KB";
	}

	/// <summary>Round 9B: the reviewer inbox page.</summary>
	public class TrainingReviewInboxView : TrainingRecordsPageView
	{
		public TrainingReviewInbox Inbox { get; set; } = new TrainingReviewInbox();

		public bool InfoRequested { get; set; }

		public int? CategoryId { get; set; }

		public string MemberUserId { get; set; }

		public string Search { get; set; }

		/// <summary>The members whose submissions the reviewer sees in the list (the member filter).</summary>
		public List<TrainingRecordMemberOption> Members { get; set; } = new List<TrainingRecordMemberOption>();
	}

	/// <summary>Round 9B: one submission for its member (respond) or a reviewer (decide).</summary>
	public class TrainingSubmissionPageView : TrainingRecordsPageView
	{
		public TrainingSubmissionDetail Detail { get; set; } = new TrainingSubmissionDetail();

		/// <summary>The reviewer page (true) or the member's page (false).</summary>
		public bool AsReviewer { get; set; }

		public TrainingSubmissionReviewStates? State => TrainingSubmissionRules.StateOf(Detail?.Record);

		public bool CanRespond => Detail?.IsOwner == true && Detail.Record?.Status == (int)LearningRecordStatuses.Pending;

		/// <summary>The latest reviewer note the member should read (the reason, or what is needed).</summary>
		public string ReviewerNote => Detail?.Record?.ReviewNote;

		public string AcceptAttribute { get; set; }
	}

	/// <summary>Round 9B: the member's own submissions.</summary>
	public class TrainingMySubmissionsView : TrainingRecordsPageView
	{
		public List<LearningRecord> Submissions { get; set; } = new List<LearningRecord>();

		public bool SubmissionsDisabled { get; set; }
	}

	/// <summary>Round 9B: the home and My Training next-action item: submissions to review, and the member's own needing more information.</summary>
	public class PendingTrainingReviewsView
	{
		public int AwaitingCount { get; set; }

		public List<LearningRecord> Waiting { get; set; } = new List<LearningRecord>();

		public List<LearningRecord> InfoNeeded { get; set; } = new List<LearningRecord>();

		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public Department Department { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;
	}
}
