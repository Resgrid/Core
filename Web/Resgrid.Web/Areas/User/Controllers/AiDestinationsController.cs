using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The View Call / Update Call destination advice panel (call-intake-and-ai-destinations-plan.md D4): the call's Enhanced AI advice, an
	/// "Ask Enhanced AI" request, Apply (sets the call's destination) and Dismiss. Reading needs the right to view the call; asking,
	/// applying and dismissing need the right to edit it. The same rules as v4 AiDestinations.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class AiDestinationsController : SecureBaseController
	{
		private readonly IAiDestinationAdvisorService _advisor;
		private readonly ICallsService _callsService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;

		public AiDestinationsController(IAiDestinationAdvisorService advisor, ICallsService callsService, Resgrid.Model.Services.IAuthorizationService authorizationService)
		{
			_advisor = advisor;
			_callsService = callsService;
			_authorizationService = authorizationService;
		}

		/// <summary>The call's open advice (or none), whether the feature is available and whether the member may act on it.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Advice(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
				return Json(new { success = false, available = false });

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			if (!await _advisor.IsAvailableAsync(DepartmentId))
				return Json(new { success = true, available = false });

			var canEdit = await _authorizationService.CanUserEditCallAsync(UserId, callId);
			return Json(new { success = true, available = true, canApply = canEdit, advice = await ShapeAsync(await _advisor.GetOpenAdviceAsync(DepartmentId, callId)) });
		}

		/// <summary>"Ask Enhanced AI": advises now (bounded to about 20 seconds).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Ask([FromForm] int callId, CancellationToken cancellationToken)
		{
			var call = await _callsService.GetCallByIdAsync(callId, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
				return Json(new { success = false, outcome = AiDestinationOutcomes.CallNotOpen });

			if (!await _authorizationService.CanUserEditCallAsync(UserId, callId))
				return Unauthorized();

			var result = await _advisor.AdviseNowAsync(DepartmentId, callId, UserId, cancellationToken);
			return Json(new { success = result.Success, outcome = result.Outcome, canApply = true, advice = await ShapeAsync(result.Advice) });
		}

		/// <summary>Sets the call's destination to the recommendation.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Apply([FromForm] string adviceId, CancellationToken cancellationToken)
		{
			var advice = string.IsNullOrWhiteSpace(adviceId) ? null : await _advisor.GetAdviceByIdAsync(DepartmentId, adviceId.Trim());
			if (advice == null)
				return Json(new { success = false, outcome = AiDestinationOutcomes.NotFound });

			if (!await _authorizationService.CanUserEditCallAsync(UserId, advice.CallId))
				return Unauthorized();

			var result = await _advisor.ApplyAsync(DepartmentId, advice.AiDestinationAdviceId, UserId, cancellationToken);
			return Json(new { success = result.Success, outcome = result.Outcome, canApply = true, advice = await ShapeAsync(result.Advice) });
		}

		/// <summary>Dismisses the advice, with an optional reason for the call's notes.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Dismiss([FromForm] string adviceId, [FromForm] string reason, CancellationToken cancellationToken)
		{
			var advice = string.IsNullOrWhiteSpace(adviceId) ? null : await _advisor.GetAdviceByIdAsync(DepartmentId, adviceId.Trim());
			if (advice == null)
				return Json(new { success = false, outcome = AiDestinationOutcomes.NotFound });

			if (!await _authorizationService.CanUserEditCallAsync(UserId, advice.CallId))
				return Unauthorized();

			var result = await _advisor.DismissAsync(DepartmentId, advice.AiDestinationAdviceId, UserId, reason, cancellationToken);
			return Json(new { success = result.Success, outcome = result.Outcome, canApply = true, advice = await ShapeAsync(result.Advice) });
		}

		private async Task<object> ShapeAsync(AiDestinationAdvice advice)
		{
			if (advice == null)
				return null;

			var details = await _advisor.DescribeAsync(DepartmentId, advice);
			return new
			{
				id = advice.AiDestinationAdviceId,
				callId = advice.CallId,
				currentPoiId = advice.CurrentPoiId,
				currentPoiName = details.CurrentPoiName,
				recommendedPoiId = advice.RecommendedPoiId,
				recommendedPoiName = details.RecommendedPoiName,
				recommendedPoiStatus = details.RecommendedPoiStatus,
				requiredCapabilities = details.RequiredCapabilities.Select(c => new { id = c.Key, name = c.Value }).ToList(),
				rationale = advice.Rationale,
				confidence = advice.Confidence,
				isChange = advice.IsChange,
				isConfident = advice.IsChange && advice.Confidence >= Math.Clamp(AiDestinationConfig.MinimumConfidence, 0, 1),
				state = advice.State,
				createdOn = DateTime.SpecifyKind(advice.CreatedOn, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
				trigger = advice.ReviewTrigger
			};
		}
	}
}
