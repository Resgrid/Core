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
		private readonly ICommandAccessService _commandAccessService;

		public IncidentRolesController(IIncidentCommandService incidentCommandService, ICommandAccessService commandAccessService)
		{
			_incidentCommandService = incidentCommandService;
			_commandAccessService = commandAccessService;
		}

		/// <summary>Whether the caller may read this incident's board: Security &gt; View Command Boards, or being on the incident.</summary>
		private Task<bool> CanViewBoardAsync(int callId) => _incidentCommandService.CanViewBoardAsync(DepartmentId, callId, UserId);

		/// <summary>A refusal the apps treat as final (403), never as an expired session (401).</summary>
		private ObjectResult Refused() => StatusCode(StatusCodes.Status403Forbidden, "You are not authorized for this incident command action.");
		#endregion Members and Constructors

		/// <summary>Assigns a Resgrid user to a functional incident-command role.</summary>
		[HttpPost("AssignRole")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
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
		[Authorize(Policy = ResgridResources.Command_View)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand, Entity = IncidentEntityKind.IncidentRoleAssignment, EntityIdName = "incidentRoleAssignmentId")]
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
			if (!await CanViewBoardAsync(callId))
				return Refused();

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
			// What this member can actually do here — the same checks the write endpoints apply — so an app that gates its
			// controls on this never offers a write the server refuses. ViewBoard says whether they may read the board at all
			// (the department-wide View Command Boards permission, or being on the incident). Never refused: a member who
			// may do nothing gets 0.
			var caps = await _incidentCommandService.GetEffectiveCapabilitiesAsync(DepartmentId, callId, UserId);
			if (caps != IncidentCapabilities.None || await CanViewBoardAsync(callId))
				caps |= IncidentCapabilities.ViewBoard;

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
