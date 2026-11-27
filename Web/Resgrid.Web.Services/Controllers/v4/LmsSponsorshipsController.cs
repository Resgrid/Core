using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// LMS sponsorships over Department Links (round 10 A3, T5 sponsored seats): the sponsorships the department gives and receives, starting
	/// one for a linked department, changing its seat limit and ending it (either side). Managing needs Manage Content Sets (permission 136); the
	/// sponsorship service re-checks it, the link (it must share Training) and the sponsor's add-on on every call.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsSponsorshipsController : V4AuthenticatedApiControllerbase
	{
		private readonly IContentSetService _sets;
		private readonly ILmsSponsorshipService _sponsorships;

		public LmsSponsorshipsController(IContentSetService sets, ILmsSponsorshipService sponsorships)
		{
			_sets = sets;
			_sponsorships = sponsorships;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task<ContentSetActor> ActorAsync() => _sets.GetActorAsync(DepartmentId, UserId);

		private ActionResult<LmsSponsorshipActionResult> Answer(LmsSponsorshipErrors error)
		{
			if (error == LmsSponsorshipErrors.NotFound)
				return NotFound();
			if (error == LmsSponsorshipErrors.NotAuthorized)
				return Forbid();
			return Ok(Populate(new LmsSponsorshipActionResult { Error = error == LmsSponsorshipErrors.None ? null : error.ToString() }, 1,
				error == LmsSponsorshipErrors.None ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>The sponsorships the department gives and receives, and the paying one it receives (if any).</summary>
		[HttpGet("GetSponsorships")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<LmsSponsorshipsResult>> GetSponsorships()
		{
			var actor = await ActorAsync();
			if (!actor.CanManage)
				return Forbid();
			var result = new LmsSponsorshipsResult();
			result.Data.Sponsorships = (await _sponsorships.GetForDepartmentAsync(DepartmentId) ?? new List<LmsSponsorshipView>()).Select(LmsSponsorshipData.From).Where(s => s != null).ToList();
			var grant = await _sponsorships.GetGrantAsync(DepartmentId);
			if (grant?.Sponsorship != null)
				result.Data.Grant = new LmsSponsorshipGrantData { LmsSponsorshipId = grant.Sponsorship.LmsSponsorshipId, SponsorName = grant.SponsorName, Capacity = grant.Capacity };
			return Ok(Populate(result, result.Data.Sponsorships.Count));
		}

		/// <summary>Starts sponsoring a linked department's LMS seats for this department's course sets (a free-plan department can be sponsored).</summary>
		[HttpPost("Sponsor")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<LmsSponsorshipActionResult>> Sponsor([FromBody] LmsSponsorInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.SponsoredDepartmentId <= 0 || (input.SeatLimit.HasValue && input.SeatLimit.Value < 0))
				return BadRequest();
			return Answer(await _sponsorships.SponsorAsync(await ActorAsync(), input.SponsoredDepartmentId, input.SeatLimit, cancellationToken));
		}

		/// <summary>Changes (or with null removes) the seat limit of a sponsorship the department gives.</summary>
		[HttpPost("SetSeatLimit")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSponsorshipActionResult>> SetSeatLimit([FromBody] LmsSponsorshipSeatLimitInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.LmsSponsorshipId <= 0 || (input.SeatLimit.HasValue && input.SeatLimit.Value < 0))
				return BadRequest();
			return Answer(await _sponsorships.SetSeatLimitAsync(await ActorAsync(), input.LmsSponsorshipId, input.SeatLimit, cancellationToken));
		}

		/// <summary>Ends a sponsorship (either side); the sponsored copies lapse under the add-on's lapse rules from now.</summary>
		[HttpPost("EndSponsorship")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSponsorshipActionResult>> EndSponsorship([FromBody] LmsSponsorshipIdInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.LmsSponsorshipId <= 0)
				return BadRequest();
			return Answer(await _sponsorships.EndAsync(await ActorAsync(), input.LmsSponsorshipId, cancellationToken));
		}
	}
}
