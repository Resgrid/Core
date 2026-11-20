using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.AiDestinations;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Enhanced AI destination advice (call-intake-and-ai-destinations-plan.md D4): the destination the model recommends for a call from
	/// its nature, note and call notes and the department's rule-ranked destinations. Advice never changes the call by itself. Anyone who
	/// can view the call can read the advice; asking for it, applying it (which sets the call's destination) and dismissing it need the
	/// right to edit the call. Live changes arrive on the eventing hub as <c>aiDestinationAdviceUpdated(callId, payload)</c>.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class AiDestinationsController : V4AuthenticatedApiControllerbase
	{
		private readonly IAiDestinationAdvisorService _advisor;
		private readonly ICallsService _callsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public AiDestinationsController(IAiDestinationAdvisorService advisor, ICallsService callsService, Model.Services.IAuthorizationService authorizationService)
		{
			_advisor = advisor;
			_callsService = callsService;
			_authorizationService = authorizationService;
		}

		/// <summary>The call's open destination advice, whether the feature is available, and whether the caller may act on it.</summary>
		/// <param name="callId">The call.</param>
		[HttpGet("GetAdvice")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<AiDestinationAdviceResult>> GetAdvice(int callId)
		{
			if (callId <= 0)
				return BadRequest();

			var result = new AiDestinationAdviceResult();
			var call = await _callsService.GetCallByIdAsync(callId, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			result.Data.Available = await _advisor.IsAvailableAsync(DepartmentId);
			if (result.Data.Available)
			{
				result.Data.CanApply = await _authorizationService.CanUserEditCallAsync(UserId, callId);
				result.Data.Advice = await ConvertAsync(await _advisor.GetOpenAdviceAsync(DepartmentId, callId));
			}

			return Ok(Done(result));
		}

		/// <summary>
		/// "Ask Enhanced AI": advises on the call's destination now (bounded to about 20 seconds) and returns the advice. When nothing the
		/// model reads changed since the open advice, that advice comes back with Outcome "Unchanged". When the feature is unavailable or
		/// the model could not answer, Advice is the existing open advice (if any) and Outcome says why.
		/// </summary>
		[HttpPost("RequestAdvice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<AiDestinationAdviceResult>> RequestAdvice([FromBody] RequestAiDestinationAdviceInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(input.CallId, false);
			var result = new AiDestinationAdviceResult();
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (!await _authorizationService.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var advised = await _advisor.AdviseNowAsync(DepartmentId, input.CallId, UserId, cancellationToken);
			result.Data.Available = advised.Outcome is not (AiDestinationOutcomes.Disabled or AiDestinationOutcomes.NotEntitled or AiDestinationOutcomes.Unconfigured
				or AiDestinationOutcomes.ProtectionUnsupported);
			result.Data.Outcome = advised.Outcome;
			result.Data.CanApply = true;
			result.Data.Advice = await ConvertAsync(advised.Advice);

			return Ok(Done(result));
		}

		/// <summary>Sets the call's destination to the advice's recommended POI (clearing a free destination), notes who did it on the call, and marks the advice Applied.</summary>
		[HttpPost("ApplyAdvice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<AiDestinationAdviceResult>> ApplyAdvice([FromBody] ApplyAiDestinationAdviceInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.AdviceId))
				return BadRequest();

			var (advice, denied) = await AuthorizeAdviceAsync(input.AdviceId);
			if (denied != null)
				return denied;

			var result = new AiDestinationAdviceResult();
			if (advice == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var applied = await _advisor.ApplyAsync(DepartmentId, advice.AiDestinationAdviceId, UserId, cancellationToken);
			return Ok(Done(await FillAsync(result, applied)));
		}

		/// <summary>Dismisses the advice. A dismissed change is noted on the call, with the reason when one is given.</summary>
		[HttpPost("DismissAdvice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<AiDestinationAdviceResult>> DismissAdvice([FromBody] DismissAiDestinationAdviceInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.AdviceId))
				return BadRequest();

			var (advice, denied) = await AuthorizeAdviceAsync(input.AdviceId);
			if (denied != null)
				return denied;

			var result = new AiDestinationAdviceResult();
			if (advice == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var dismissed = await _advisor.DismissAsync(DepartmentId, advice.AiDestinationAdviceId, UserId, input.Reason, cancellationToken);
			return Ok(Done(await FillAsync(result, dismissed)));
		}

		/// <summary>The department's advice and, when it exists, whether the caller may edit its call (Unauthorized otherwise).</summary>
		private async Task<(AiDestinationAdvice Advice, ActionResult Denied)> AuthorizeAdviceAsync(string adviceId)
		{
			var advice = await _advisor.GetAdviceByIdAsync(DepartmentId, adviceId.Trim());
			if (advice == null)
				return (null, null);

			if (!await _authorizationService.CanUserEditCallAsync(UserId, advice.CallId))
				return (null, Unauthorized());

			return (advice, null);
		}

		private async Task<AiDestinationAdviceResult> FillAsync(AiDestinationAdviceResult result, AiDestinationAdviceOutcome outcome)
		{
			result.Data.Available = true;
			result.Data.Outcome = outcome.Outcome;
			result.Data.CanApply = true;
			result.Data.Advice = await ConvertAsync(outcome.Advice);
			if (outcome.Outcome == AiDestinationOutcomes.NotFound)
				result.Status = ResponseHelper.NotFound;
			return result;
		}

		private static AiDestinationAdviceResult Done(AiDestinationAdviceResult result)
		{
			result.PageSize = result.Data.Advice == null ? 0 : 1;
			result.Status ??= ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private async Task<AiDestinationAdviceData> ConvertAsync(AiDestinationAdvice advice)
		{
			if (advice == null)
				return null;

			var details = await _advisor.DescribeAsync(DepartmentId, advice);
			return ConvertAdvice(advice, details);
		}

		/// <summary>The v4 shape of an advice row with its display names.</summary>
		public static AiDestinationAdviceData ConvertAdvice(AiDestinationAdvice advice, AiDestinationAdviceDetails details)
		{
			details ??= new AiDestinationAdviceDetails();
			return new AiDestinationAdviceData
			{
				Id = advice.AiDestinationAdviceId,
				CallId = advice.CallId,
				CurrentPoiId = advice.CurrentPoiId,
				CurrentPoiName = details.CurrentPoiName,
				RecommendedPoiId = advice.RecommendedPoiId,
				RecommendedPoiName = details.RecommendedPoiName,
				RecommendedPoiStatus = details.RecommendedPoiStatus,
				RequiredCapabilities = details.RequiredCapabilities.Select(c => new AiDestinationCapabilityData { Id = c.Key, Name = c.Value }).ToList(),
				Rationale = advice.Rationale,
				Confidence = advice.Confidence,
				IsChange = advice.IsChange,
				IsConfident = advice.IsChange && advice.Confidence >= Math.Clamp(AiDestinationConfig.MinimumConfidence, 0, 1),
				State = advice.State,
				CreatedOn = DateTime.SpecifyKind(advice.CreatedOn, DateTimeKind.Utc),
				Trigger = advice.ReviewTrigger
			};
		}
	}
}
