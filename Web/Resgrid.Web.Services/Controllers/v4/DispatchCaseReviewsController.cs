using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.DispatchCaseReviews;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Dispatch case review feedback for Omni (Protocols P5 QA): the member's own sent reviews, one review with its scored questions,
	/// instructions and criteria, the acknowledgment (once) and the one reply. Officers sample and score on the web. A member sees only their
	/// own reviews, and only once sent. The older apps get no client change; their members open the review from the notification link.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DispatchCaseReviewsController : V4AuthenticatedApiControllerbase
	{
		private readonly IDispatchCaseReviewService _reviews;

		public DispatchCaseReviewsController(IDispatchCaseReviewService reviews)
		{
			_reviews = reviews;
		}

		/// <summary>The member's own sent dispatch case reviews, newest first (no items or criteria: GetMyReview has them).</summary>
		[HttpGet("GetMyReviews")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetMyDispatchCaseReviewsResult>> GetMyReviews()
		{
			var result = new GetMyDispatchCaseReviewsResult();
			foreach (var review in await _reviews.GetMineAsync(DepartmentId, UserId))
				result.Data.Add(ToData(review, UserId, null));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>One of the member's own sent reviews, with what was scored. Not found for anyone else's review or one not sent yet.</summary>
		[HttpGet("GetMyReview")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetMyDispatchCaseReviewResult>> GetMyReview(int reviewId)
		{
			var detail = await _reviews.GetDetailAsync(DepartmentId, UserId, reviewId);
			// The API is the dispatcher's surface: a reviewer reads other people's reviews on the web.
			if (detail?.Review == null || !DispatchCaseReviewRules.DispatcherMaySee(detail.Review, UserId))
				return NotFound();

			var result = new GetMyDispatchCaseReviewResult { Data = ToData(detail.Review, UserId, detail) };
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Acknowledges the member's review (once); a comment, when given and there is no reply yet, is the one reply.</summary>
		[HttpPost("Acknowledge")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<DispatchCaseReviewFeedbackResult>> Acknowledge([FromBody] DispatchCaseReviewFeedbackInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ReviewId <= 0)
				return BadRequest();
			return Ok(Feedback(await _reviews.AcknowledgeAsync(DepartmentId, UserId, input.ReviewId, input.Comment, cancellationToken)));
		}

		/// <summary>The member's one reply to their review.</summary>
		[HttpPost("Reply")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<DispatchCaseReviewFeedbackResult>> Reply([FromBody] DispatchCaseReviewFeedbackInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ReviewId <= 0 || string.IsNullOrWhiteSpace(input.Comment))
				return BadRequest();
			return Ok(Feedback(await _reviews.CommentAsync(DepartmentId, UserId, input.ReviewId, input.Comment, cancellationToken)));
		}

		private static DispatchCaseReviewFeedbackResult Feedback(DispatchCaseReviewResults outcome)
		{
			var result = new DispatchCaseReviewFeedbackResult
			{
				Data = new DispatchCaseReviewFeedbackResultData { Outcome = outcome.ToString(), Accepted = outcome == DispatchCaseReviewResults.Done },
				PageSize = 1,
				Status = outcome == DispatchCaseReviewResults.Done ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private static string Compliance(int value) => (DispatchCaseCompliance)value switch
		{
			DispatchCaseCompliance.Compliant => "compliant",
			DispatchCaseCompliance.Partial => "partial",
			DispatchCaseCompliance.NotCompliant => "not_compliant",
			DispatchCaseCompliance.NotApplicable => "not_applicable",
			_ => "not_scored"
		};

		internal static DispatchCaseReviewResultData ToData(DispatchCaseReview review, string userId, DispatchCaseReviewDetail detail)
		{
			var data = new DispatchCaseReviewResultData
			{
				ReviewId = review.DispatchCaseReviewId,
				CallId = review.CallId,
				CallNumber = review.CallNumber,
				CallName = review.CallName,
				CallLoggedOn = review.CallLoggedOn,
				ProtocolId = review.DispatchProtocolId,
				ProtocolVersionId = review.ProtocolVersionId,
				ProtocolCode = review.ProtocolCode,
				ProtocolName = review.ProtocolName,
				VersionLabel = review.VersionLabel,
				Determinant = review.Determinant,
				Overridden = review.Overridden,
				Status = review.Status == (int)DispatchCaseReviewStatuses.Acknowledged ? "acknowledged" : "sent",
				Score = review.Score,
				QuestionCompliance = DispatchCaseScoring.Percent(review.QuestionCount, review.QuestionPoints),
				InstructionCompliance = DispatchCaseScoring.Percent(review.InstructionCount, review.InstructionPoints),
				SentOn = review.SentOn,
				AcknowledgedOn = review.AcknowledgedOn,
				ReviewerComments = review.ReviewerComments,
				DispatcherComment = review.DispatcherComment,
				DispatcherCommentOn = review.DispatcherCommentOn,
				CanAcknowledge = DispatchCaseReviewRules.CanAcknowledge(review, userId),
				CanReply = DispatchCaseReviewRules.CanComment(review, userId)
			};

			if (detail != null)
			{
				data.Items = detail.Items.Select(i => new DispatchCaseReviewItemData
				{
					Key = i.NodeKey, Kind = i.IsInstruction ? "instruction" : "question", Text = i.Text, Answer = i.Answer, Compliance = Compliance(i.Compliance), Note = i.Note
				}).ToList();
				data.Criteria = detail.Criteria.Select(c =>
				{
					var finding = detail.Findings.FirstOrDefault(f => f.Key == c.Key);
					return new DispatchCaseReviewCriterionData { Key = c.Key, Text = c.Text, Weight = c.Weight, Score = finding?.Score, Note = finding?.Note, Derived = DispatchCaseRubrics.IsDerived(c.Key) };
				}).ToList();
			}

			return data;
		}
	}
}
