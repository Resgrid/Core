using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Dispatch case review (Protocols P5 QA, free with Protocols): officers sample calls by protocol, version, determinant, dispatcher and
	/// dates, score the recorded case (P4) question by question and instruction by instruction against a Records QA rubric, and send the
	/// review to the dispatcher, who sees only their own reviews, acknowledges each once and may reply once. Reviewers hold Review Dispatch
	/// Cases (134) or are department admins, and never score their own calls. Reports give compliance by protocol, version, dispatcher and
	/// month.
	/// </summary>
	public interface IDispatchCaseReviewService
	{
		/// <summary>Whether the member reviews dispatch cases (a current member holding 134 or a department admin) and is a department admin.</summary>
		Task<(bool IsReviewer, bool IsAdmin)> GetAccessAsync(int departmentId, string userId);

		/// <summary>
		/// Who may review dispatch cases (for the assignee picker): department admins and the holders of the Review Dispatch Cases row (its roles,
		/// group admins, or everyone), active members only, compared case-insensitively.
		/// </summary>
		Task<HashSet<string>> GetReviewerUserIdsAsync(int departmentId);

		/// <summary>The department's dispatch case rubrics (reviewers). Records rubrics are not listed.</summary>
		Task<List<RmsQualityRubric>> GetRubricsAsync(int departmentId, string userId, bool includeInactive = false);

		/// <summary>Creates the dispatch case rubric from the template in the given language (department admins; reviewers when there is none).</summary>
		Task<RmsQualityRubric> CreateTemplateRubricAsync(int departmentId, string userId, DispatchCaseRubricTexts texts, CancellationToken cancellationToken = default);

		/// <summary>Saves a dispatch case rubric's name, active flag and criteria (department admins). Null when refused or invalid.</summary>
		Task<RmsQualityRubric> SaveRubricAsync(int departmentId, string userId, string rubricId, string name, bool isActive, List<RmsQualityCriterion> criteria,
			CancellationToken cancellationToken = default);

		/// <summary>Counts what a sample would draw from (nothing is written).</summary>
		Task<DispatchCaseSampleResult> PreviewSampleAsync(int departmentId, string userId, DispatchCaseSampleRequest request);

		/// <summary>Draws the sample and queues one review per picked case.</summary>
		Task<DispatchCaseSampleResult> SampleAsync(int departmentId, string userId, DispatchCaseSampleRequest request, CancellationToken cancellationToken = default);

		Task<DispatchCaseReviewSummary> GetSummaryAsync(int departmentId, string userId);

		/// <summary>The officers' queue (reviewers); <paramref name="mineOnly"/> keeps the reviews assigned to the member.</summary>
		Task<List<DispatchCaseReview>> GetQueueAsync(int departmentId, string userId, bool mineOnly);

		/// <summary>Assigns an open review to the member (unassigned ones; department admins may take any).</summary>
		Task<DispatchCaseReviewResults> TakeAsync(int departmentId, string userId, int reviewId, CancellationToken cancellationToken = default);

		/// <summary>Takes an open review out of the queue (the call can be sampled again).</summary>
		Task<DispatchCaseReviewResults> RemoveAsync(int departmentId, string userId, int reviewId, CancellationToken cancellationToken = default);

		/// <summary>A review with its case, graph and timeline, for a reviewer, or for its dispatcher once sent. Null when neither.</summary>
		Task<DispatchCaseReviewDetail> GetDetailAsync(int departmentId, string userId, int reviewId);

		/// <summary>Saves scores as a draft, or sends the review to the dispatcher when <paramref name="send"/> and everything is scored.</summary>
		Task<DispatchCaseReviewResults> SaveScoresAsync(int departmentId, string userId, int reviewId, DispatchCaseScoreInput input, bool send,
			CancellationToken cancellationToken = default);

		/// <summary>The member's own sent reviews ("My case reviews").</summary>
		Task<List<DispatchCaseReview>> GetMineAsync(int departmentId, string userId);

		/// <summary>The dispatcher acknowledges their review once; an optional comment is the one reply.</summary>
		Task<DispatchCaseReviewResults> AcknowledgeAsync(int departmentId, string userId, int reviewId, string comment, CancellationToken cancellationToken = default);

		/// <summary>The dispatcher's one reply to a sent review.</summary>
		Task<DispatchCaseReviewResults> CommentAsync(int departmentId, string userId, int reviewId, string comment, CancellationToken cancellationToken = default);

		/// <summary>Compliance by protocol, version, dispatcher and month (reviewers). Null when refused.</summary>
		Task<DispatchCaseComplianceReport> GetReportAsync(int departmentId, string userId, DispatchCaseReportFilter filter);
	}

	/// <summary>Direct notices of dispatch case review (EventTypes.DispatchCaseReviewNotice 76, push code "PQ:{reviewId}"); active members only.</summary>
	public interface IDispatchCaseReviewNotifier
	{
		/// <summary>Tells the dispatcher their case was reviewed, with the link to the review.</summary>
		Task<bool> NotifySentAsync(DispatchCaseReview review, CancellationToken cancellationToken = default);

		/// <summary>Tells the reviewer the dispatcher replied.</summary>
		Task<bool> NotifyReplyAsync(DispatchCaseReview review, CancellationToken cancellationToken = default);
	}
}
