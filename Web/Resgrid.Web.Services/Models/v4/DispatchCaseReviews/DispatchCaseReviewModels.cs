using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.DispatchCaseReviews
{
	/// <summary>One of the member's own sent dispatch case reviews (Protocols P5 QA; Omni "My case reviews").</summary>
	public class DispatchCaseReviewResultData
	{
		public int ReviewId { get; set; }
		public int CallId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public DateTime CallLoggedOn { get; set; }
		public int ProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public string ProtocolCode { get; set; }
		public string ProtocolName { get; set; }
		public string VersionLabel { get; set; }
		public string Determinant { get; set; }
		public bool Overridden { get; set; }

		/// <summary>sent, acknowledged.</summary>
		public string Status { get; set; }

		public int? Score { get; set; }
		public double? QuestionCompliance { get; set; }
		public double? InstructionCompliance { get; set; }
		public DateTime? SentOn { get; set; }
		public DateTime? AcknowledgedOn { get; set; }
		public string ReviewerComments { get; set; }
		public string DispatcherComment { get; set; }
		public DateTime? DispatcherCommentOn { get; set; }
		public bool CanAcknowledge { get; set; }
		public bool CanReply { get; set; }

		/// <summary>The questions and instructions scored (GetMyReview only).</summary>
		public List<DispatchCaseReviewItemData> Items { get; set; } = new List<DispatchCaseReviewItemData>();

		/// <summary>The rubric's criteria with their scores (GetMyReview only).</summary>
		public List<DispatchCaseReviewCriterionData> Criteria { get; set; } = new List<DispatchCaseReviewCriterionData>();
	}

	public class DispatchCaseReviewItemData
	{
		public string Key { get; set; }

		/// <summary>question, instruction.</summary>
		public string Kind { get; set; }

		public string Text { get; set; }
		public string Answer { get; set; }

		/// <summary>not_scored, compliant, partial, not_compliant, not_applicable.</summary>
		public string Compliance { get; set; }

		public string Note { get; set; }
	}

	public class DispatchCaseReviewCriterionData
	{
		public string Key { get; set; }
		public string Text { get; set; }
		public int Weight { get; set; }
		public int? Score { get; set; }
		public string Note { get; set; }
		public bool Derived { get; set; }
	}

	public class GetMyDispatchCaseReviewsResult : StandardApiResponseV4Base
	{
		public List<DispatchCaseReviewResultData> Data { get; set; } = new List<DispatchCaseReviewResultData>();
	}

	public class GetMyDispatchCaseReviewResult : StandardApiResponseV4Base
	{
		public DispatchCaseReviewResultData Data { get; set; }
	}

	/// <summary>Acknowledge a review (once), optionally with the one reply; or reply alone.</summary>
	public class DispatchCaseReviewFeedbackInput
	{
		public int ReviewId { get; set; }
		public string Comment { get; set; }
	}

	public class DispatchCaseReviewFeedbackResultData
	{
		/// <summary>The <see cref="Resgrid.Model.DispatchCaseReviewResults"/> name (Done, AlreadyAcknowledged, AlreadyCommented, NotFound, …).</summary>
		public string Outcome { get; set; }
		public bool Accepted { get; set; }
	}

	public class DispatchCaseReviewFeedbackResult : StandardApiResponseV4Base
	{
		public DispatchCaseReviewFeedbackResultData Data { get; set; }
	}
}
