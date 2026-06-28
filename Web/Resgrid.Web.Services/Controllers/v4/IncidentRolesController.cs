using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.Services.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Incident-scoped functional command roles (§3.11). Assign Resgrid users to ICS positions (Staging Officer,
	/// Rehab Officer, Section Chiefs, ...) which drive each user's specialized app view and capabilities.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentRolesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentCommandService _incidentCommandService;

		public IncidentRolesController(IIncidentCommandService incidentCommandService)
		{
			_incidentCommandService = incidentCommandService;
		}
		#endregion Members and Constructors

		/// <summary>Assigns a Resgrid user to a functional incident-command role.</summary>
		[HttpPost("AssignRole")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentRoleResult>> AssignRole([FromBody] IncidentRoleAssignment assignment)
		{
			if (assignment == null || string.IsNullOrWhiteSpace(assignment.IncidentCommandId) || string.IsNullOrWhiteSpace(assignment.UserId))
				return BadRequest();

			assignment.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentRoleResult();
			var saved = await _incidentCommandService.AssignIncidentRoleAsync(assignment, UserId, CancellationToken.None);

			if (saved == null)
			{
				// Parent incident command not found / not owned by the caller's department.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Removes a functional incident-command role assignment.</summary>
		[HttpPost("RemoveRole/{incidentRoleAssignmentId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> RemoveRole(string incidentRoleAssignmentId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.RemoveIncidentRoleAsync(DepartmentId, incidentRoleAssignmentId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the active functional role assignments for a call.</summary>
		[HttpGet("GetRoles/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentRolesResult>> GetRoles(int callId)
		{
			var result = new ICModels.IncidentRolesResult();
			result.Data = await _incidentCommandService.GetIncidentRolesAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the current user's effective capabilities for an incident (drives the app's view gating).</summary>
		[HttpGet("GetMyCapabilities/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCapabilitiesResult>> GetMyCapabilities(int callId)
		{
			var caps = await _incidentCommandService.GetCapabilitiesForUserAsync(DepartmentId, callId, UserId);

			var result = new ICModels.IncidentCapabilitiesResult();
			result.Value = (int)caps;

			foreach (IncidentCapabilities cap in Enum.GetValues(typeof(IncidentCapabilities)))
			{
				if (cap == IncidentCapabilities.None || cap == IncidentCapabilities.All)
					continue;

				if (caps.HasFlag(cap))
					result.Capabilities.Add(cap.ToString());
			}

			result.PageSize = result.Capabilities.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
