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
using Resgrid.Web.Services.Models.v4.IntakeScreening;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Intake spam handling (call-intake-and-ai-destinations-plan.md §9, M0326). A dispatcher who may close a pending intake call can
	/// dismiss it as spam: it is cancelled (nobody is notified; nobody was dispatched) and its SMS conversation ends without texting the
	/// sender. Only when the request asks for it (<c>BlockSender</c>, the default) is the sender also blocked from the department's
	/// intake channels for IntakeConfig.SpamBlockDays; with <c>BlockSender</c> false the sender can still reach intake. Admins lift
	/// blocks on the web Intake Channels page. Calls carry their screening on the v4 call model (<c>IntakeScreening</c>).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IntakeScreeningController : V4AuthenticatedApiControllerbase
	{
		private readonly IIntakeScreeningService _screening;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public IntakeScreeningController(IIntakeScreeningService screening, Model.Services.IAuthorizationService authorizationService)
		{
			_screening = screening;
			_authorizationService = authorizationService;
		}

		/// <summary>Cancels a pending intake call as spam and (by default) blocks its sender.</summary>
		/// <param name="input">The call, and whether to block its sender.</param>
		/// <param name="cancellationToken">Request cancellation.</param>
		/// <returns>
		/// The outcome: Dismissed; NotFound or NotPending (Status not_found); or Failed (Status failure: nothing was dismissed and the
		/// call is still pending, try again; <c>SenderBlocked</c> says whether a block was made before the failure).
		/// </returns>
		[HttpPost("DismissAsSpam")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<DismissIntakeCallResult>> DismissAsSpam([FromBody] DismissIntakeCallInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || input.CallId <= 0)
				return BadRequest();

			// The same right Omni's "Cancel call" on a pending call needs.
			if (!await _authorizationService.CanUserCloseCallAsync(UserId, input.CallId, DepartmentId))
				return Unauthorized();

			var outcome = await _screening.DismissAsSpamAsync(DepartmentId, input.CallId, UserId, input.BlockSender, cancellationToken);

			var result = new DismissIntakeCallResult();
			result.Data.CallId = input.CallId;
			result.Data.Outcome = outcome.Result.ToString();
			result.Data.SenderBlocked = outcome.Block != null;
			result.Data.SenderNotBlockable = outcome.SenderNotBlockable;
			result.Data.BlockedUntilUtc = outcome.Block?.ExpiresOn;
			result.PageSize = outcome.Result == IntakeDismissResults.Dismissed ? 1 : 0;
			result.Status = outcome.Result == IntakeDismissResults.Dismissed ? ResponseHelper.Updated
				: outcome.Result == IntakeDismissResults.Failed ? ResponseHelper.Failure
				: ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}
	}
}
