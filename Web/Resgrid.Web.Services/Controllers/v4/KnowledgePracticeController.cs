using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Knowledge;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Scenario practice on a protocol (K3 tutor) for Omni: status, a scenario from the protocol's version in force, and the member's answers
	/// scored against the sections it cites. Practice only: nothing is written to the transcript, a certification or a quiz. 404 unless the
	/// department has the tutor (Enhanced AI knowledge answers and the LMS add-on); the older apps are not changed.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class KnowledgePracticeController : V4AuthenticatedApiControllerbase
	{
		private readonly IKnowledgePracticeService _practice;

		public KnowledgePracticeController(IKnowledgePracticeService practice)
		{
			_practice = practice;
		}

		private KnowledgeReader Reader() => new KnowledgeReader
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
			CanViewProtocols = User.HasClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.View),
			CanViewTrainings = User.HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View),
			CanViewDocuments = User.HasClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View)
		};

		private static T Populate<T>(T result, int pageSize) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Whether practice is available to the member right now.</summary>
		[HttpGet("GetStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<KnowledgePracticeStatusResult>> GetStatus(CancellationToken cancellationToken)
		{
			var status = await _practice.GetStatusAsync(Reader(), cancellationToken);
			if (status == null || status.Reason == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var result = new KnowledgePracticeStatusResult();
			result.Data.Available = status.Available;
			result.Data.Reason = status.Available ? null : status.Reason;
			return Ok(Populate(result, 1));
		}

		/// <summary>A new practice scenario from the protocol's version in force; every question cites the sections it is asked from.</summary>
		[HttpPost("Start")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<KnowledgePracticeScenarioResult>> Start([FromBody] KnowledgePracticeStartInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			var scenario = await _practice.StartAsync(Reader(), input.ProtocolId, cancellationToken);
			if (scenario == null || scenario.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var result = new KnowledgePracticeScenarioResult();
			var data = result.Data;
			data.Outcome = scenario.Outcome;
			data.ExchangeId = scenario.ExchangeId;
			data.ProtocolId = scenario.ProtocolId;
			data.ProtocolVersionId = scenario.ProtocolVersionId;
			data.VersionLabel = scenario.VersionLabel;
			data.Scenario = scenario.Scenario;
			data.Questions = scenario.Questions.Select(q => new KnowledgePracticeQuestionData { Key = q.Key, Text = q.Text, Refs = q.Refs.ToList() }).ToList();
			data.Citations = scenario.Citations.Select(Map).ToList();
			data.PromptVersion = scenario.PromptVersion;
			return Ok(Populate(result, data.Questions.Count));
		}

		/// <summary>Scores the member's answers to one of their own scenarios against the cited sections only. Do not include patient or personal details.</summary>
		[HttpPost("Score")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<KnowledgePracticeScoreResult>> Score([FromBody] KnowledgePracticeScoreInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ExchangeId) || input.Answers == null)
				return BadRequest();

			var answers = input.Answers.Where(a => a != null).Take(8).Select(a => new KnowledgePracticeAnswer { Key = a.Key, Text = a.Text }).ToList();
			var scored = await _practice.ScoreAsync(Reader(), input.ExchangeId.Trim(), answers, cancellationToken);
			if (scored == null || scored.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var result = new KnowledgePracticeScoreResult();
			var data = result.Data;
			data.Outcome = scored.Outcome;
			data.ExchangeId = scored.ExchangeId;
			data.Total = scored.Total;
			data.Max = scored.Max;
			data.Items = scored.Items.Select(i => new KnowledgePracticeItemData
			{
				Key = i.Key, Score = i.Score, MaxScore = KnowledgePracticeItemScore.MaxScore, Feedback = i.Feedback, Refs = i.Refs.ToList()
			}).ToList();
			data.Citations = scored.Citations.Select(Map).ToList();
			data.QuotesDose = scored.QuotesDose;
			data.DosingToolUrl = scored.DosingToolUrl;
			data.PromptVersion = scored.PromptVersion;
			return Ok(Populate(result, data.Items.Count));
		}

		private static KnowledgeCitationData Map(KnowledgeCitation c) => new KnowledgeCitationData
		{
			Ref = c.Ref,
			SourceKind = c.SourceKind,
			ItemId = c.ItemId,
			VersionId = c.VersionId,
			VersionLabel = c.VersionLabel,
			Anchor = c.Anchor,
			HeadingPath = c.HeadingPath,
			Title = c.Title,
			EffectiveOnUtc = c.EffectiveOn,
			Excerpt = c.Excerpt,
			OfflineCopyStale = c.OfflineCopyStale,
			WebPath = KnowledgeLinks.WebPath(c.SourceKind, c.ItemId, c.VersionId, c.Anchor, Config.SystemBehaviorConfig.AcademyUrl)
		};
	}
}
