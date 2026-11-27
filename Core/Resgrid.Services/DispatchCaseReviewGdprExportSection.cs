using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Protocols P5 QA in a member's GDPR export ("dispatch_case_reviews.json"): the reviews of the member's own dispatch cases that were sent
	/// to them, with the scores, the per-question and per-instruction verdicts, the reviewer's comments and the member's acknowledgment and
	/// reply: exactly what "My case reviews" shows. Reviews still in the officers' queue and other members' reviews are never included.
	/// </summary>
	public class DispatchCaseReviewGdprExportSection : IGdprExportSection
	{
		private readonly IDispatchCaseReviewsRepository _reviews;

		public DispatchCaseReviewGdprExportSection(IDispatchCaseReviewsRepository reviews)
		{
			_reviews = reviews;
		}

		public string EntryName => "dispatch_case_reviews.json";

		public async Task<object> BuildAsync(string userId, int departmentId)
		{
			var rows = (await _reviews.GetSentForDispatcherAsync(departmentId, userId, 5000)) ?? new System.Collections.Generic.List<DispatchCaseReview>();
			return rows.Where(r => r.DepartmentId == departmentId && DispatchCaseReviewRules.DispatcherMaySee(r, userId)).Select(r => new
			{
				r.DispatchCaseReviewId,
				r.CallId,
				r.CallNumber,
				r.CallLoggedOn,
				r.ProtocolCode,
				r.ProtocolName,
				r.VersionLabel,
				r.Determinant,
				r.Overridden,
				r.RubricName,
				r.Score,
				QuestionCompliance = DispatchCaseScoring.Percent(r.QuestionCount, r.QuestionPoints),
				InstructionCompliance = DispatchCaseScoring.Percent(r.InstructionCount, r.InstructionPoints),
				// Never the caller's answers (ADP catalog 39): they are the call's case content, not the dispatcher's personal data.
				Items = DispatchCaseReviewItem.Parse(r.ItemsJson).Select(i => new { i.NodeKey, i.Kind, i.Text, Compliance = ((DispatchCaseCompliance)i.Compliance).ToString(), i.Note }).ToList(),
				r.ReviewerComments,
				r.SentOn,
				r.AcknowledgedOn,
				r.DispatcherComment,
				r.DispatcherCommentOn
			}).ToList();
		}
	}
}
