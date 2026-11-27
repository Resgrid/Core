using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>The officers' queue (Protocols P5 QA).</summary>
	public class DispatchCaseQueueModel
	{
		public DispatchCaseReviewSummary Summary { get; set; } = new DispatchCaseReviewSummary();
		public List<DispatchCaseReview> Reviews { get; set; } = new List<DispatchCaseReview>();
		public bool MineOnly { get; set; }
		public bool IsAdmin { get; set; }
		public string UserId { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) ? name : userId;
	}

	/// <summary>The sampling form, its picklists and the last preview.</summary>
	public class DispatchCaseSampleModel
	{
		public DispatchCaseSampleForm Form { get; set; } = new DispatchCaseSampleForm();
		public List<SelectListItem> Protocols { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Versions { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Dispatchers { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Reviewers { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Rubrics { get; set; } = new List<SelectListItem>();
		public DispatchCaseSampleResult Preview { get; set; }
		public string Error { get; set; }
	}

	public class DispatchCaseSampleForm
	{
		/// <summary>Department-local dates (inclusive), yyyy-MM-dd.</summary>
		public DateTime? From { get; set; }
		public DateTime? To { get; set; }
		public int? ProtocolId { get; set; }
		public int? VersionId { get; set; }
		public string Determinant { get; set; }
		public string DispatcherUserId { get; set; }

		/// <summary>"count" or "percent".</summary>
		public string Mode { get; set; } = "count";
		public int? Count { get; set; } = 10;
		public int? Percent { get; set; } = 5;
		public string RubricId { get; set; }
		public string AssignToUserId { get; set; }
	}

	/// <summary>One review page: the case beside the protocol version's graph, the scoring form, the dispatcher's acknowledgment.</summary>
	public class DispatchCaseReviewModel
	{
		public DispatchCaseReviewDetail Detail { get; set; }
		public string DispatcherName { get; set; }
		public string ReviewerName { get; set; }
		public string AssigneeName { get; set; }
		public string OverriddenByName { get; set; }

		/// <summary>The version's case flow rendered by the protocol renderer (everything authored is encoded there).</summary>
		public string GraphHtml { get; set; }

		/// <summary>ADP: the grant a *Revealed render was posted with; the scoring form carries it on so a score is saved with the answers revealed.</summary>
		public string ProtectedGrant { get; set; }

		public DateTime? ProtectedGrantExpiresOnUtc { get; set; }
		public List<string> PathKeys { get; set; } = new List<string>();
		public string Message { get; set; }
		public string Error { get; set; }
	}

	public class DispatchCaseScoreForm
	{
		public List<DispatchCaseItemPost> Items { get; set; } = new List<DispatchCaseItemPost>();
		public List<DispatchCaseFindingPost> Findings { get; set; } = new List<DispatchCaseFindingPost>();
		public string Comments { get; set; }
	}

	public class DispatchCaseItemPost
	{
		public string Key { get; set; }
		public int Compliance { get; set; }
		public string Note { get; set; }
	}

	public class DispatchCaseFindingPost
	{
		public string Key { get; set; }
		public int? Score { get; set; }
		public string Note { get; set; }
	}

	/// <summary>"My case reviews": the dispatcher's own sent reviews.</summary>
	public class MyDispatchCaseReviewsModel
	{
		public List<DispatchCaseReview> Reviews { get; set; } = new List<DispatchCaseReview>();
		public bool IsReviewer { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The compliance report and its filter.</summary>
	public class DispatchCaseReportModel
	{
		public DispatchCaseReportForm Form { get; set; } = new DispatchCaseReportForm();
		public DispatchCaseComplianceReport Report { get; set; }
		public List<SelectListItem> Protocols { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Versions { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Dispatchers { get; set; } = new List<SelectListItem>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string Error { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) ? name : userId;
	}

	public class DispatchCaseReportForm
	{
		public DateTime? From { get; set; }
		public DateTime? To { get; set; }
		public int? ProtocolId { get; set; }
		public int? VersionId { get; set; }
		public string DispatcherUserId { get; set; }
	}

	/// <summary>The department's dispatch case rubrics (Records QA rubrics marked for dispatch cases).</summary>
	public class DispatchCaseRubricsModel
	{
		public List<RmsQualityRubric> Rubrics { get; set; } = new List<RmsQualityRubric>();
		public string EditId { get; set; }
		public DispatchCaseRubricForm Form { get; set; } = new DispatchCaseRubricForm();
		public bool IsAdmin { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	public class DispatchCaseRubricForm
	{
		public string RubricId { get; set; }
		public string Name { get; set; }
		public bool IsActive { get; set; } = true;
		public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>();
	}
}
