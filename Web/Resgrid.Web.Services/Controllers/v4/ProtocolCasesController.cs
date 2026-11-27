using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.ProtocolCases;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Protocols P4 for Omni's dispatch form (the older apps get no client change and keep their read-only protocol list, R-S7): the
	/// department's case flows, a preview walk, instruction audio, the Enhanced AI protocol suggestion for a call, the determinant
	/// override after a call is saved (reason required) and the caller follow-up text over the call's intake SMS thread. A case is saved
	/// with the call through SaveCall/EditCall's optional ProtocolCases.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolCasesController : V4AuthenticatedApiControllerbase
	{
		private readonly IProtocolCaseService _cases;
		private readonly IProtocolAiSuggestionService _ai;
		private readonly IAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;

		public ProtocolCasesController(IProtocolCaseService cases, IProtocolAiSuggestionService ai, IAuthorizationService authorization,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer)
		{
			_cases = cases;
			_ai = ai;
			_authorization = authorization;
			_localizer = localizer;
		}

		/// <summary>The department's published, enabled Dispatch protocols with a valid case flow (the version in force's graph).</summary>
		[HttpGet("GetCaseFlows")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<GetProtocolCaseFlowsResult>> GetCaseFlows()
		{
			var result = new GetProtocolCaseFlowsResult
			{
				Data = (await _cases.GetDefinitionsAsync(DepartmentId)).Select(ProtocolCaseFlowData.From).ToList(),
				AiAvailable = await _ai.IsAvailableAsync(DepartmentId),
				Status = ResponseHelper.Success
			};
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>One protocol's case flow; 404 when it has none in force.</summary>
		[HttpGet("GetCaseFlow")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<GetProtocolCaseFlowResult>> GetCaseFlow(int protocolId)
		{
			var definition = await _cases.GetDefinitionAsync(DepartmentId, protocolId);
			if (definition == null)
				return NotFound();

			var result = new GetProtocolCaseFlowResult { Data = ProtocolCaseFlowData.From(definition), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Walks a protocol's case flow with answers (a preview; nothing is saved). The determinant is the server's.</summary>
		[HttpPost("EvaluateCase")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<EvaluateProtocolCaseResult>> EvaluateCase([FromBody] EvaluateProtocolCaseInput input)
		{
			if (input == null || input.ProtocolId <= 0)
				return BadRequest();

			var walked = await _cases.EvaluateAsync(DepartmentId, input.ProtocolId, input.Case ?? new ProtocolCaseInput());
			var outcome = walked.Outcome;
			var result = new EvaluateProtocolCaseResult
			{
				Data = new ProtocolCaseEvaluationData
				{
					Status = walked.Status.ToString(),
					CurrentNodeKey = walked.CurrentNodeKey,
					Steps = walked.Steps,
					InstructionKeys = walked.InstructionKeys,
					OutcomeNodeKey = outcome?.Key,
					Determinant = outcome?.Determinant,
					SuggestedPriority = outcome?.PriorityId,
					SuggestedPriorityName = outcome?.PriorityName,
					ResponseHint = outcome?.ResponseHint,
					PostDispatch = outcome?.PostDispatch,
					Error = walked.Error == ProtocolCaseErrors.None ? null : walked.Error.ToString(),
					ErrorNodeKey = walked.ErrorNodeKey
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The audio of an instruction step marked "speak" (Piper TTS) as URLs to play in order; empty when there is none.</summary>
		[HttpGet("GetInstructionAudio")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<GetInstructionAudioResult>> GetInstructionAudio(int protocolId, string nodeKey, CancellationToken cancellationToken = default)
		{
			var urls = await _cases.GetInstructionAudioAsync(DepartmentId, protocolId, nodeKey, cancellationToken);
			var result = new GetInstructionAudioResult { Data = urls.Select(u => u.ToString()).ToList(), PageSize = urls.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Enhanced AI: suggests a protocol and prefills its answers from the call's text or intake SMS thread. Nothing is attached; the
		/// dispatcher checks the answers and saves them with EditCall (ProtocolCases with AiSuggestionId), which records Source = AI.
		/// </summary>
		[HttpPost("SuggestProtocol")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<SuggestProtocolResult>> SuggestProtocol([FromBody] SuggestProtocolInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || input.CallId <= 0 || !await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var suggested = await _ai.SuggestAsync(DepartmentId, input.CallId, UserId, cancellationToken);
			var data = new ProtocolSuggestionData { Outcome = suggested.Outcome.ToString(), Message = Text("AiSuggest" + suggested.Outcome) };
			if (suggested.Suggestion != null)
			{
				data.SuggestionId = suggested.Suggestion.ProtocolAiSuggestionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
				data.ProtocolId = suggested.Suggestion.DispatchProtocolId.ToString(System.Globalization.CultureInfo.InvariantCulture);
				data.Code = suggested.ProtocolCode;
				data.Name = suggested.ProtocolName;
				data.Confidence = suggested.Suggestion.Confidence;
				data.Rationale = suggested.Suggestion.Rationale;
				data.Answers = suggested.Suggestion.Answers;
			}

			var result = new SuggestProtocolResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Overrides (or restores) the determinant of a protocol on a saved call; a reason is required for any other determinant.</summary>
		[HttpPost("OverrideDeterminant")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<ProtocolCaseUpdateApiResult>> OverrideDeterminant([FromBody] OverrideProtocolDeterminantInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || input.CallId <= 0 || !await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var updated = await _cases.OverrideDeterminantAsync(DepartmentId, input.CallId, input.CallProtocolId, input.Determinant, input.Reason, UserId, cancellationToken);
			return Ok(Updated(updated, "Case"));
		}

		/// <summary>Texts a case instruction to the caller over the call's intake SMS thread (text only, logged on the call).</summary>
		[HttpPost("SendCallerInstruction")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<ProtocolCaseUpdateApiResult>> SendCallerInstruction([FromBody] SendProtocolCallerInstructionInput input, CancellationToken cancellationToken = default)
		{
			if (input == null || input.CallId <= 0 || !await _authorization.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var updated = await _cases.SendCallerInstructionAsync(DepartmentId, input.CallId, input.CallProtocolId, input.NodeKey, UserId, cancellationToken);
			return Ok(Updated(updated, "CaseCaller"));
		}

		private ProtocolCaseUpdateApiResult Updated(ProtocolCaseUpdateResult updated, string messagePrefix)
		{
			var result = new ProtocolCaseUpdateApiResult
			{
				Data = new ProtocolCaseUpdateResultData
				{
					Outcome = updated.Outcome.ToString(),
					Message = Text(messagePrefix + updated.Outcome),
					Determinant = updated.CallProtocol?.Determinant,
					Case = updated.Case
				},
				PageSize = 1,
				Status = updated.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private string Text(string key)
		{
			var value = _localizer?[key];
			return value == null || value.ResourceNotFound ? key : value.Value;
		}
	}
}
