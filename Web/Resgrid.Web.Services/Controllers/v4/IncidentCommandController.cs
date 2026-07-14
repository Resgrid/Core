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
	/// Live incident command: establish/transfer/close command, edit the command structure (lanes), assign
	/// resources, manage objectives, timers, map annotations, and read the action timeline for a Call.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentCommandController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentCommandService _incidentCommandService;

		public IncidentCommandController(IIncidentCommandService incidentCommandService)
		{
			_incidentCommandService = incidentCommandService;
		}
		#endregion Members and Constructors

		#region Command lifecycle

		/// <summary>Establishes command on a call, optionally seeding lanes from a command definition.</summary>
		// Bootstrap: intentionally NOT [RequiresIncidentCapability]. EstablishCommand CREATES the command, so at call
		// time no command/commander/role (hence no IncidentCapabilities) exists yet — GetCapabilitiesForUserAsync would
		// return None and 403 every establish. Department-level [Authorize(Command_Create)] is the correct gate here;
		// the incident-scoped capability checks apply to the lifecycle endpoints that operate on an existing command.
		[HttpPost("EstablishCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> EstablishCommand([FromBody] ICModels.EstablishCommandInput input)
		{
			if (input == null || input.CallId <= 0)
				return BadRequest();

			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.EstablishCommandAsync(DepartmentId, input.CallId, UserId, input.CommandDefinitionId, CancellationToken.None);

			if (command == null)
			{
				// Call not found / not owned by the caller's department.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the full live command board snapshot for a call.</summary>
		[HttpGet("GetCommandBoard/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCommandBoardResult>> GetCommandBoard(int callId)
		{
			var result = new ICModels.IncidentCommandBoardResult();
			var board = await _incidentCommandService.GetCommandBoardAsync(DepartmentId, callId);

			if (board == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = board;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Transfers command to another user.</summary>
		[HttpPost("TransferCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.CommandTransferResult>> TransferCommand([FromBody] ICModels.TransferCommandInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId) || string.IsNullOrWhiteSpace(input.ToUserId))
				return BadRequest();

			var result = new ICModels.CommandTransferResult();
			var transfer = await _incidentCommandService.TransferCommandAsync(DepartmentId, input.IncidentCommandId, UserId, input.ToUserId, input.Notes, CancellationToken.None);

			if (transfer == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = transfer;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Closes command on an incident.</summary>
		[HttpPut("CloseCommand/{incidentCommandId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> CloseCommand(string incidentCommandId)
		{
			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.CloseCommandAsync(DepartmentId, incidentCommandId, UserId, CancellationToken.None);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Updates the incident action plan.</summary>
		[HttpPut("UpdateActionPlan")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateActionPlan([FromBody] ICModels.UpdateActionPlanInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.UpdateActionPlanAsync(DepartmentId, input.IncidentCommandId, input.ActionPlan, UserId, CancellationToken.None);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the personnel accountability / PAR status (Green/Warning/Critical) for the incident.</summary>
		[HttpGet("GetAccountability/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.CommandAccountabilityResult>> GetAccountability(int callId)
		{
			var result = new ICModels.CommandAccountabilityResult();
			result.Data = await _incidentCommandService.GetAccountabilityForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Runs a personnel accountability (PAR) sweep for the call, raising workflow + real-time alerts for any
		/// member newly overdue (Critical). Returns the user ids flagged this pass. Idempotent — repeated calls
		/// only re-alert after a member checks in and lapses again.
		/// </summary>
		[HttpPost("EvaluateAccountability/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAccountability)]
		public async Task<ActionResult<ICModels.EvaluateAccountabilityResult>> EvaluateAccountability(int callId)
		{
			var result = new ICModels.EvaluateAccountabilityResult();
			result.Data = await _incidentCommandService.EvaluateCriticalParAsync(DepartmentId, callId, CancellationToken.None);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Command lifecycle

		#region Structure (lanes)

		/// <summary>Creates or updates a command structure lane.</summary>
		[HttpPost("SaveNode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageStructure)]
		public async Task<ActionResult<ICModels.CommandNodeResult>> SaveNode([FromBody] CommandStructureNode node)
		{
			if (node == null || string.IsNullOrWhiteSpace(node.IncidentCommandId))
				return BadRequest();

			node.DepartmentId = DepartmentId;

			var result = new ICModels.CommandNodeResult();
			var saved = await _incidentCommandService.SaveNodeAsync(node, UserId, CancellationToken.None);

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

		/// <summary>Removes a command structure lane.</summary>
		[HttpDelete("DeleteNode/{commandStructureNodeId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> DeleteNode(string commandStructureNodeId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.DeleteNodeAsync(DepartmentId, commandStructureNodeId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Structure (lanes)

		#region Resource assignments

		/// <summary>Assigns a resource (own/mutual-aid/ad-hoc unit or person) to a lane.</summary>
		[HttpPost("AssignResource")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.AssignResources)]
		public async Task<ActionResult<ICModels.ResourceAssignmentResult>> AssignResource([FromBody] ResourceAssignment assignment)
		{
			if (assignment == null || string.IsNullOrWhiteSpace(assignment.IncidentCommandId))
				return BadRequest();

			assignment.DepartmentId = DepartmentId;

			var result = new ICModels.ResourceAssignmentResult();

			Resgrid.Model.ResourceAssignment saved;
			try
			{
				saved = await _incidentCommandService.AssignResourceAsync(assignment, UserId, CancellationToken.None);
			}
			catch (CommandRequirementsNotMetException ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				// The target lane forces unit-type/personnel-role requirements the resource doesn't meet.
				result.Status = ResponseHelper.Failure;
				result.Message = ex.Message;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.Message = saved.RequirementsWarningMessage; // advisory (non-forced) requirements notice, if any
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Moves a resource assignment to a different lane.</summary>
		[HttpPost("MoveResource")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.ResourceAssignmentResult>> MoveResource([FromBody] ICModels.MoveResourceInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ResourceAssignmentId) || string.IsNullOrWhiteSpace(input.TargetNodeId))
				return BadRequest();

			var result = new ICModels.ResourceAssignmentResult();

			Resgrid.Model.ResourceAssignment assignment;
			try
			{
				assignment = await _incidentCommandService.MoveResourceAsync(DepartmentId, input.ResourceAssignmentId, input.TargetNodeId, UserId, CancellationToken.None);
			}
			catch (CommandRequirementsNotMetException ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				// The target lane forces unit-type/personnel-role requirements the resource doesn't meet.
				result.Status = ResponseHelper.Failure;
				result.Message = ex.Message;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			if (assignment == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = assignment;
			result.Message = assignment.RequirementsWarningMessage; // advisory (non-forced) requirements notice, if any
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Releases a resource assignment.</summary>
		[HttpPost("ReleaseResource/{resourceAssignmentId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> ReleaseResource(string resourceAssignmentId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.ReleaseResourceAsync(DepartmentId, resourceAssignmentId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Resource assignments

		#region Objectives

		/// <summary>Creates or updates a tactical objective / benchmark.</summary>
		[HttpPost("SaveObjective")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageObjectives)]
		public async Task<ActionResult<ICModels.TacticalObjectiveResult>> SaveObjective([FromBody] TacticalObjective objective)
		{
			if (objective == null || string.IsNullOrWhiteSpace(objective.IncidentCommandId))
				return BadRequest();

			objective.DepartmentId = DepartmentId;

			var result = new ICModels.TacticalObjectiveResult();
			var saved = await _incidentCommandService.SaveObjectiveAsync(objective, UserId, CancellationToken.None);

			if (saved == null)
			{
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

		/// <summary>Marks a tactical objective complete.</summary>
		[HttpPost("CompleteObjective/{tacticalObjectiveId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.TacticalObjectiveResult>> CompleteObjective(string tacticalObjectiveId)
		{
			var result = new ICModels.TacticalObjectiveResult();
			var objective = await _incidentCommandService.CompleteObjectiveAsync(DepartmentId, tacticalObjectiveId, UserId, CancellationToken.None);

			if (objective == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = objective;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Objectives

		#region Timers

		/// <summary>Starts a scene/benchmark/role timer.</summary>
		[HttpPost("StartTimer")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageTimers)]
		public async Task<ActionResult<ICModels.IncidentTimerResult>> StartTimer([FromBody] IncidentTimer timer)
		{
			if (timer == null || string.IsNullOrWhiteSpace(timer.IncidentCommandId))
				return BadRequest();

			timer.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentTimerResult();
			var saved = await _incidentCommandService.StartTimerAsync(timer, UserId, CancellationToken.None);

			if (saved == null)
			{
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

		/// <summary>Acknowledges a timer (resets its next-due time).</summary>
		[HttpPost("AcknowledgeTimer/{incidentTimerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentTimerResult>> AcknowledgeTimer(string incidentTimerId)
		{
			var result = new ICModels.IncidentTimerResult();
			var timer = await _incidentCommandService.AcknowledgeTimerAsync(DepartmentId, incidentTimerId, UserId, CancellationToken.None);

			if (timer == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = timer;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Timers

		#region Map annotations

		/// <summary>Creates or updates a real-time map annotation.</summary>
		[HttpPost("SaveAnnotation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAnnotations)]
		public async Task<ActionResult<ICModels.IncidentMapAnnotationResult>> SaveAnnotation([FromBody] IncidentMapAnnotation annotation)
		{
			if (annotation == null || string.IsNullOrWhiteSpace(annotation.IncidentCommandId))
				return BadRequest();

			annotation.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentMapAnnotationResult();
			var saved = await _incidentCommandService.SaveAnnotationAsync(annotation, UserId, CancellationToken.None);

			if (saved == null)
			{
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

		/// <summary>Removes a map annotation.</summary>
		[HttpDelete("DeleteAnnotation/{incidentMapAnnotationId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> DeleteAnnotation(string incidentMapAnnotationId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.DeleteAnnotationAsync(DepartmentId, incidentMapAnnotationId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Map annotations

		#region Timeline

		/// <summary>Gets the append-only command (ICS-201) timeline for a call.</summary>
		[HttpGet("GetTimeline/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.CommandTimelineResult>> GetTimeline(int callId)
		{
			var result = new ICModels.CommandTimelineResult();
			result.Data = await _incidentCommandService.GetTimelineForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Timeline
	}
}
