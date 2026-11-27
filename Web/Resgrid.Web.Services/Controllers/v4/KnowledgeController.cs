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
	/// Department knowledge answers with citations (K1) for Omni: status, ask (optionally about one protocol or training, or about the
	/// protocol version a call cites) and thumbs up/down. 404 unless the department has Ai.Knowledge (Enhanced AI); the free tier keeps the
	/// deterministic Search endpoints. Answers come only from what the member may open.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class KnowledgeController : V4AuthenticatedApiControllerbase
	{
		private readonly IAiKnowledgeService _knowledge;

		public KnowledgeController(IAiKnowledgeService knowledge)
		{
			_knowledge = knowledge;
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

		/// <summary>Whether Ask is available to the member right now.</summary>
		[HttpGet("GetStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<KnowledgeStatusResult>> GetStatus(CancellationToken cancellationToken)
		{
			var status = await _knowledge.GetStatusAsync(Reader(), cancellationToken);
			if (status == null || status.Reason == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var result = new KnowledgeStatusResult();
			result.Data.Available = status.Available;
			result.Data.Reason = status.Available ? null : status.Reason;
			result.Data.QuestionsKeptLocal = status.QuestionsKeptLocal;
			result.Data.CanSeeGaps = status.CanSeeGaps;
			return Ok(Populate(result, 1));
		}

		/// <summary>
		/// Asks the department's knowledge. Every part of an answer cites the sources it rests on; "NotCovered" comes with the closest items;
		/// doses are only ever quoted from a source, never computed. Do not include patient or personal details in the question.
		/// </summary>
		[HttpPost("Ask")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<KnowledgeAskResult>> Ask([FromBody] KnowledgeAskInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Question))
				return BadRequest();

			var offline = new Dictionary<string, string>(System.StringComparer.Ordinal);
			foreach (var held in (input.OfflineVersions ?? new List<KnowledgeOfflineVersionInput>()).Take(500))
				if (held != null && !string.IsNullOrWhiteSpace(held.ItemId) && !string.IsNullOrWhiteSpace(held.VersionId))
					offline[held.SourceKind.ToString(CultureInfo.InvariantCulture) + ":" + held.ItemId.Trim()] = held.VersionId.Trim();

			var answer = await _knowledge.AskAsync(new KnowledgeAskRequest
			{
				Reader = Reader(),
				Question = input.Question,
				Surface = input.ScopeKind == (int)KnowledgeSourceKinds.Academy && string.IsNullOrWhiteSpace(input.ScopeItemId) ? (int)KnowledgeAskSurfaces.Help
					: input.ScopeKind == (int)KnowledgeSourceKinds.Course && !string.IsNullOrWhiteSpace(input.ScopeItemId) ? (int)KnowledgeAskSurfaces.Course
					: input.CallId.HasValue ? (int)KnowledgeAskSurfaces.Call : (int)KnowledgeAskSurfaces.Api,
				// An item scope, or the whole Academy corpus (A3 "How do I…?"): ScopeKind 5 without an item.
				ScopeKind = input.ScopeKind.HasValue && System.Enum.IsDefined(typeof(KnowledgeSourceKinds), input.ScopeKind.Value) &&
					(!string.IsNullOrWhiteSpace(input.ScopeItemId) || input.ScopeKind.Value == (int)KnowledgeSourceKinds.Academy) ? input.ScopeKind : null,
				ScopeItemId = string.IsNullOrWhiteSpace(input.ScopeItemId) ? null : input.ScopeItemId.Trim(),
				// K3 "Ask this course": the course version the learner takes (ScopeKind 4); the course's own source checks it is theirs.
				ScopeVersionId = string.IsNullOrWhiteSpace(input.ScopeItemId) || string.IsNullOrWhiteSpace(input.ScopeVersionId) ? null : input.ScopeVersionId.Trim(),
				CallId = input.CallId,
				CallProtocolId = input.CallProtocolId,
				OfflineVersions = offline
			}, cancellationToken);

			if (answer == null || answer.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();
			if (answer.Outcome == KnowledgeOutcomes.Invalid)
				return BadRequest();

			var result = new KnowledgeAskResult();
			var data = result.Data;
			data.Outcome = answer.Outcome;
			data.Parts = answer.Parts.Select(p => new KnowledgePartData { Text = p.Text, Refs = p.Refs.ToList() }).ToList();
			data.Conflicts = answer.Conflicts.Select(c => new KnowledgeConflictData { Note = c.Note, Refs = c.Refs.ToList() }).ToList();
			data.Citations = answer.Citations.Select(Map).ToList();
			data.Nearest = answer.Nearest.Select(Map).ToList();
			data.RetrievalMode = answer.RetrievalMode;
			data.EmbeddingsUnavailable = answer.EmbeddingsUnavailable;
			data.QuestionKeptLocal = answer.QuestionKeptLocal;
			data.QuotesDose = answer.QuotesDose;
			data.DosingToolUrl = answer.DosingToolUrl;
			data.OfflineCopyStale = answer.OfflineCopyStale;
			data.ExchangeId = answer.ExchangeId;
			data.PromptVersion = answer.PromptVersion;
			data.CallContextUsed = answer.CallContextUsed;
			return Ok(Populate(result, data.Parts.Count));
		}

		/// <summary>Thumbs up or down on one of the member's own answers; a thumbs-down goes to the knowledge gaps report.</summary>
		[HttpPost("Feedback")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<KnowledgeFeedbackResult>> Feedback([FromBody] KnowledgeFeedbackInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ExchangeId))
				return BadRequest();

			var result = new KnowledgeFeedbackResult();
			result.Data.Recorded = await _knowledge.FeedbackAsync(Reader(), input.ExchangeId, input.Helpful, (int)KnowledgeAskSurfaces.Api, cancellationToken);
			return Ok(Populate(result, 1));
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
