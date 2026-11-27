using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	/// <summary>One of the member's learning requirements (T3) with its state; Omni's My Training "requirements" list.</summary>
	public class LmsMyRequirementData
	{
		public int RequirementId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }

		/// <summary>Course, Training, CategoryHours, Path or Evaluations.</summary>
		public string Kind { get; set; }

		/// <summary>The course, training, category, path or skill the member is asked to finish.</summary>
		public string TargetName { get; set; }

		/// <summary>Compliant, Pending, AtRisk, Overdue, Exempt, OnLeave or NotLicensed.</summary>
		public string State { get; set; }

		/// <summary>The open due date (leave and lapse applied), or the next renewal when compliant; UTC.</summary>
		public DateTime? DueOn { get; set; }

		public DateTime? LastCompletedOn { get; set; }

		/// <summary>Hours or evaluations done this period (counted requirements); 1/0 otherwise.</summary>
		public decimal Progress { get; set; }

		public decimal Required { get; set; }

		public int? CourseId { get; set; }
		public int? PathId { get; set; }
		public int? TrainingId { get; set; }
	}

	public class LmsMyRequirementsResult : StandardApiResponseV4Base
	{
		public List<LmsMyRequirementData> Data { get; set; } = new List<LmsMyRequirementData>();
	}

	/// <summary>One step of a learning path as the learner sees it.</summary>
	public class LmsMyPathStepData
	{
		public int Order { get; set; }

		/// <summary>Course (an LMS course or an Academy lesson) or Training.</summary>
		public string Kind { get; set; }

		public int TargetId { get; set; }
		public string Title { get; set; }

		/// <summary>Locked, Available or Done.</summary>
		public string State { get; set; }

		public DateTime? CompletedOn { get; set; }
		public List<int> Prerequisites { get; set; } = new List<int>();
		public int? ElectiveGroup { get; set; }

		/// <summary>The open enrollment to play (LmsController.GetCourse), when the course step is enrolled.</summary>
		public int? EnrollmentId { get; set; }
	}

	/// <summary>One of the member's learning paths with progress; Omni's My Training "paths" list.</summary>
	public class LmsMyPathData
	{
		public int PathId { get; set; }
		public string Title { get; set; }
		public bool IsAcademy { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public int Done { get; set; }
		public int Needed { get; set; }
		public List<LmsMyPathStepData> Steps { get; set; } = new List<LmsMyPathStepData>();
	}

	public class LmsMyPathsResult : StandardApiResponseV4Base
	{
		public List<LmsMyPathData> Data { get; set; } = new List<LmsMyPathData>();
	}

	/// <summary>One reason a learning item is recommended (K3).</summary>
	public class LmsRecommendationReasonData
	{
		/// <summary>RequirementOverdue, RequirementAtRisk, RequirementDue, PathInProgress, CompetencyMissing, CompetencyAged or RoleAudience.</summary>
		public string Reason { get; set; }

		/// <summary>What the reason names: the requirement, the competency or the role.</summary>
		public string Detail { get; set; }

		public DateTime? DueOn { get; set; }
	}

	/// <summary>One recommended course or learning path, best first (K3).</summary>
	public class LmsRecommendationData
	{
		/// <summary>"Course:12" or "Path:3": the key the explanation texts use.</summary>
		public string Key { get; set; }

		/// <summary>Course or Path.</summary>
		public string TargetKind { get; set; }

		public int TargetId { get; set; }
		public string Title { get; set; }
		public int Score { get; set; }
		public DateTime? DueOn { get; set; }

		/// <summary>A Resgrid Academy lesson.</summary>
		public bool IsAcademy { get; set; }

		/// <summary>The caller's own open enrollment in the course ("Continue"); never set for another member.</summary>
		public int? EnrollmentId { get; set; }

		public List<LmsRecommendationReasonData> Reasons { get; set; } = new List<LmsRecommendationReasonData>();
	}

	public class LmsRecommendationsData
	{
		public string UserId { get; set; }

		/// <summary>An AI explanation may be asked for (ExplainRecommendations).</summary>
		public bool CanExplain { get; set; }

		public List<LmsRecommendationData> Items { get; set; } = new List<LmsRecommendationData>();
	}

	public class LmsRecommendationsResult : StandardApiResponseV4Base
	{
		public LmsRecommendationsData Data { get; set; } = new LmsRecommendationsData();
	}

	public class LmsRecommendationExplainInput
	{
		/// <summary>The member; blank for the caller.</summary>
		public string UserId { get; set; }
	}

	public class LmsRecommendationExplainData
	{
		/// <summary>Answered, or why not: Unconfigured, KeptLocal, BudgetExhausted, Busy, Unavailable, Refused or Invalid.</summary>
		public string Outcome { get; set; }

		/// <summary>One sentence per recommendation key ("Course:12").</summary>
		public Dictionary<string, string> Texts { get; set; } = new Dictionary<string, string>();

		public string ExchangeId { get; set; }
	}

	public class LmsRecommendationExplainResult : StandardApiResponseV4Base
	{
		public LmsRecommendationExplainData Data { get; set; } = new LmsRecommendationExplainData();
	}
}
