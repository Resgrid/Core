using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.Services.Helpers;
using System.Threading;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// On-demand PTT tactical voice channels scoped to an incident (§3.4). Requires the department PTT voice addon.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentVoiceController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentVoiceService _incidentVoiceService;

		public IncidentVoiceController(IIncidentVoiceService incidentVoiceService)
		{
			_incidentVoiceService = incidentVoiceService;
		}
		#endregion Members and Constructors

		/// <summary>Creates an on-demand tactical channel scoped to a call (requires the voice addon).</summary>
		[HttpPost("CreateIncidentChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageChannels)]
		public async Task<ActionResult<ICModels.IncidentVoiceChannelResult>> CreateIncidentChannel([FromBody] ICModels.CreateIncidentChannelInput input)
		{
			if (input == null || input.CallId <= 0)
				return BadRequest();

			var result = new ICModels.IncidentVoiceChannelResult();
			var channel = await _incidentVoiceService.CreateIncidentChannelAsync(DepartmentId, input.CallId, input.Name, UserId, CancellationToken.None);

			if (channel == null)
			{
				// Voice addon not enabled for the department, or the channel could not be provisioned.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = channel;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the open on-demand tactical channels for a call (visible to assigned units/users).</summary>
		[HttpGet("GetChannelsForCall/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentVoiceChannelsResult>> GetChannelsForCall(int callId)
		{
			var result = new ICModels.IncidentVoiceChannelsResult();
			result.Data = await _incidentVoiceService.GetChannelsForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Closes all open on-demand tactical channels for a call.</summary>
		[HttpPost("CloseIncidentChannels/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageChannels)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> CloseIncidentChannels(int callId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentVoiceService.CloseIncidentChannelsForCallAsync(DepartmentId, callId, UserId, CancellationToken.None);
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
