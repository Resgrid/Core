using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T3 part 2 (LMS add-on): what Omni would use for training requests and the member's competency gaps. Omni itself is out of
	// scope for this build. All times are UTC; enum fields are names.

	/// <summary>A training request (an outside class with its cost and approval).</summary>
	public class LmsTrainingRequestData
	{
		public int TrainingRequestId { get; set; }
		public string UserId { get; set; }
		public string Title { get; set; }
		public string Provider { get; set; }
		public string Location { get; set; }
		public string CourseCode { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public decimal CostTuition { get; set; }
		public decimal CostTravel { get; set; }
		public decimal CostOther { get; set; }
		public decimal? ActualCost { get; set; }
		public string Currency { get; set; }
		public string Justification { get; set; }
		/// <summary>LmsTrainingRequestStatuses name: Submitted, Approved, Rejected, Withdrawn, Completed, Cancelled.</summary>
		public string Status { get; set; }
		/// <summary>LmsTrainingRequestSteps name of the step waiting for a decision (Supervisor, TrainingOfficer); null when none.</summary>
		public string CurrentStep { get; set; }
		public DateTime SubmittedOn { get; set; }
		public DateTime? DecidedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		/// <summary>The external submission (T1) written when the member reported the class taken.</summary>
		public int? LearningRecordId { get; set; }
	}

	public class LmsTrainingRequestsResult : StandardApiResponseV4Base
	{
		public List<LmsTrainingRequestData> Data { get; set; } = new List<LmsTrainingRequestData>();
	}

	public class LmsTrainingRequestResult : StandardApiResponseV4Base
	{
		public LmsTrainingRequestData Data { get; set; }
		/// <summary>A refusal code (lms_program_*), when the action was refused.</summary>
		public string Error { get; set; }
	}

	public class LmsTrainingRequestInput
	{
		public string Title { get; set; }
		public string Provider { get; set; }
		public string Location { get; set; }
		public string CourseCode { get; set; }
		public DateTime? StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public decimal CostTuition { get; set; }
		public decimal CostTravel { get; set; }
		public decimal CostOther { get; set; }
		public string Justification { get; set; }
	}

	public class LmsTrainingRequestDecisionInput
	{
		public int TrainingRequestId { get; set; }
		public bool Approve { get; set; }
		public string Note { get; set; }
	}

	public class LmsTrainingRequestIdInput
	{
		public int TrainingRequestId { get; set; }
	}

	public class LmsTrainingRequestCompleteInput
	{
		public int TrainingRequestId { get; set; }
		public DateTime CompletedOn { get; set; }
		public decimal Hours { get; set; }
		public decimal? ActualCost { get; set; }
	}

	/// <summary>One item of the member's competency gap list.</summary>
	public class LmsCompetencyGapData
	{
		public int ItemId { get; set; }
		public int? ParentItemId { get; set; }
		public string Code { get; set; }
		public string Label { get; set; }
		/// <summary>LmsCompetencyEvidenceStates name: Current, Aged, None, NotTagged.</summary>
		public string State { get; set; }
		public DateTime? EvidenceOn { get; set; }
		public int? AgeDays { get; set; }
		public string EvidenceTitle { get; set; }
	}

	public class LmsCompetencyGapsResult : StandardApiResponseV4Base
	{
		public int FrameworkId { get; set; }
		public string FrameworkName { get; set; }
		public List<LmsCompetencyGapData> Data { get; set; } = new List<LmsCompetencyGapData>();
	}
}
