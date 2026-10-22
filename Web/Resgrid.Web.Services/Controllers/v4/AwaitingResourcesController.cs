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
using Resgrid.Web.Services.Models.v4.AwaitingResources;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Calls awaiting resources (conditional run cards): the list dispatch clients show as a banner and a screen, a call's
	/// open requirements, dismissing one, and dispatching a freed unit to a waiting call in one step. Gated behind the
	/// Dispatch.RunCardConditions feature flag.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class AwaitingResourcesController : V4AuthenticatedApiControllerbase
	{
		private readonly IOutstandingRequirementsService _outstandingRequirementsService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public AwaitingResourcesController(IOutstandingRequirementsService outstandingRequirementsService, IFeatureToggleService featureToggleService,
			IAuthorizationService authorizationService, ICallsService callsService, IUnitsService unitsService, IDepartmentSettingsService departmentSettingsService)
		{
			_outstandingRequirementsService = outstandingRequirementsService;
			_featureToggleService = featureToggleService;
			_authorizationService = authorizationService;
			_callsService = callsService;
			_unitsService = unitsService;
			_departmentSettingsService = departmentSettingsService;
		}

		/// <summary>
		/// Every active call of the department still awaiting a required resource, highest priority then oldest first, with
		/// the units now available that match each requirement (nearest first when it can be measured).
		/// </summary>
		[HttpGet("GetAwaitingCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<AwaitingResourcesResult>> GetAwaitingCalls(CancellationToken cancellationToken)
		{
			var result = new AwaitingResourcesResult { Enabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId) };

			if (result.Enabled)
				result.Data = await _outstandingRequirementsService.GetAwaitingCallsAsync(DepartmentId, cancellationToken);

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>The call's open outstanding requirements, reconciled with its dispatches first.</summary>
		[HttpGet("GetCallRequirements")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallOutstandingRequirementsResult>> GetCallRequirements(int callId, CancellationToken cancellationToken)
		{
			var result = new CallOutstandingRequirementsResult();

			if (await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId))
			{
				var call = await _callsService.GetCallByIdAsync(callId);

				if (call == null || call.DepartmentId != DepartmentId)
					return NotFound();

				if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
					return Unauthorized();

				result.Data = await _outstandingRequirementsService.GetForCallAsync(DepartmentId, callId, false, cancellationToken);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>Dismisses an open requirement the call no longer needs; the same authority as editing the call.</summary>
		[HttpPost("DismissRequirement")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<AwaitingResourcesActionResult>> DismissRequirement([FromBody] DismissOutstandingRequirementInput input, CancellationToken cancellationToken)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId))
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(input.CallId);

			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _authorizationService.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			// Never trust a posted requirement id on its own: it must be an open requirement of this call.
			var open = await _outstandingRequirementsService.GetForCallAsync(DepartmentId, input.CallId, false, cancellationToken);

			var result = new AwaitingResourcesActionResult();

			if (open.All(r => r.CallOutstandingRequirementId != input.RequirementId))
			{
				result.Success = false;
				result.Message = "That requirement is no longer open.";
			}
			else
			{
				result.Success = await _outstandingRequirementsService.DismissAsync(DepartmentId, input.RequirementId, UserId, input.Note, cancellationToken);
			}

			result.Status = result.Success ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Adds a unit to a call awaiting resources in one step (additive; only that unit is notified), which resolves the
		/// requirement it matches.
		/// </summary>
		[HttpPost("DispatchUnit")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<AwaitingResourcesActionResult>> DispatchUnit([FromBody] DispatchToAwaitingCallInput input, CancellationToken cancellationToken)
		{
			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, DepartmentId))
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(input.CallId);

			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _authorizationService.CanUserEditCallAsync(UserId, input.CallId))
				return Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(input.UnitId);

			if (unit == null || unit.DepartmentId != DepartmentId || unit.IsDeleted)
				return NotFound();

			var outcome = await _outstandingRequirementsService.DispatchUnitToCallAsync(DepartmentId, input.CallId, input.UnitId, UserId, cancellationToken);

			var result = new AwaitingResourcesActionResult { Success = outcome.Success, Message = outcome.Message, Resolved = outcome.ResolvedRequirements };
			result.Status = result.Success ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// The department's response code catalog, for the per-unit code choice on a run card proposal. Empty when the
		/// department uses no codes (the feature is then invisible).
		/// </summary>
		[HttpGet("GetResponseCodes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ResponseCodesResult>> GetResponseCodes()
		{
			var result = new ResponseCodesResult
			{
				Data = await _departmentSettingsService.GetDispatchResponseCodesAsync(DepartmentId) ?? new DispatchResponseCodesConfig()
			};

			result.PageSize = result.Data.Codes?.Count ?? 0;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
