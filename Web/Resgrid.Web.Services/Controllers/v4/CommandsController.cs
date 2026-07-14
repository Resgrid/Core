using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Command definitions: predefined incident-command templates (swimlanes) per call type, used to
	/// seed the runtime command board. House Fire vs Vehicle Incident, etc.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CommandsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICommandsService _commandsService;

		public CommandsController(ICommandsService commandsService)
		{
			_commandsService = commandsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all command definitions for the department.
		/// </summary>
		[HttpGet("GetAllCommands")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<CommandsResult>> GetAllCommands()
		{
			var result = new CommandsResult();

			var commands = await _commandsService.GetAllCommandsForDepartmentAsync(DepartmentId);

			if (commands != null && commands.Any())
			{
				foreach (var command in commands)
					result.Data.Add(ConvertCommandData(command));

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Gets a single command definition by identifier.
		/// </summary>
		[HttpGet("GetCommand/{commandDefinitionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<CommandResult>> GetCommand(int commandDefinitionId)
		{
			var result = new CommandResult();

			var command = await _commandsService.GetCommandByIdAsync(commandDefinitionId);

			if (command == null || command.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = ConvertCommandData(command);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Resolves the command definition (template) for a call type, falling back to the
		/// "Any Call Type" definition. Pass 0 to request the "Any Call Type" template directly.
		/// </summary>
		[HttpGet("GetCommandForCallType/{callTypeId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<CommandResult>> GetCommandForCallType(int callTypeId)
		{
			var result = new CommandResult();

			int? typeId = callTypeId > 0 ? callTypeId : (int?)null;
			var command = await _commandsService.GetCommandForCallTypeAsync(DepartmentId, typeId);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = ConvertCommandData(command);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Creates or updates a command definition (including its lanes).
		/// </summary>
		[HttpPost("SaveCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<ActionResult<CommandResult>> SaveCommand([FromBody] SaveCommandInput input)
		{
			var result = new CommandResult();

			if (input == null || string.IsNullOrWhiteSpace(input.Name))
				return BadRequest();

			CommandDefinition command;

			if (input.CommandDefinitionId.HasValue && input.CommandDefinitionId.Value > 0)
			{
				command = await _commandsService.GetCommandByIdAsync(input.CommandDefinitionId.Value);

				if (command == null || command.DepartmentId != DepartmentId)
				{
					result.Status = ResponseHelper.NotFound;
					ResponseHelper.PopulateV4ResponseData(result);
					return result;
				}
			}
			else
			{
				command = new CommandDefinition { DepartmentId = DepartmentId };
			}

			command.Name = input.Name;
			command.Description = input.Description;
			command.CallTypeId = input.CallTypeId;
			command.Timer = input.Timer;
			command.TimerMinutes = input.TimerMinutes;

			command.Assignments = new List<CommandDefinitionRole>();
			if (input.Lanes != null)
			{
				foreach (var lane in input.Lanes)
				{
					var role = new CommandDefinitionRole
					{
						CommandDefinitionRoleId = lane.CommandDefinitionRoleId ?? 0,
						Name = lane.Name,
						Description = lane.Description,
						LaneType = lane.LaneType,
						SortOrder = lane.SortOrder,
						MinUnitPersonnel = lane.MinUnitPersonnel,
						MaxUnitPersonnel = lane.MaxUnitPersonnel,
						MaxUnits = lane.MaxUnits,
						MinTimeInRole = lane.MinTimeInRole,
						MaxTimeInRole = lane.MaxTimeInRole,
						ForceRequirements = lane.ForceRequirements
					};

					// The API save is a full document: absent lists clear the lane's requirements.
					role.RequiredUnitTypes = (lane.RequiredUnitTypes ?? new List<int>())
						.Select(id => new CommandDefinitionRoleUnitType { UnitTypeId = id }).ToList();
					role.RequiredRoles = (lane.RequiredPersonnelRoles ?? new List<int>())
						.Select(id => new CommandDefinitionRolePersonnelRole { PersonnelRoleId = id }).ToList();

					command.Assignments.Add(role);
				}
			}

			var saved = await _commandsService.Save(command, CancellationToken.None);

			result.Data = ConvertCommandData(saved);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Deletes a command definition.
		/// </summary>
		[HttpDelete("DeleteCommand/{commandDefinitionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Delete)]
		public async Task<ActionResult<CommandResult>> DeleteCommand(int commandDefinitionId)
		{
			var result = new CommandResult();

			var command = await _commandsService.GetCommandByIdAsync(commandDefinitionId);

			if (command == null || command.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			await _commandsService.DeleteAsync(command, CancellationToken.None);

			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#region Private Helpers
		private static CommandResultData ConvertCommandData(CommandDefinition command)
		{
			var data = new CommandResultData
			{
				CommandDefinitionId = command.CommandDefinitionId,
				CallTypeId = command.CallTypeId,
				Name = command.Name,
				Description = command.Description,
				Timer = command.Timer,
				TimerMinutes = command.TimerMinutes
			};

			if (command.Assignments != null)
			{
				foreach (var lane in command.Assignments.OrderBy(x => x.SortOrder))
				{
					data.Lanes.Add(new CommandRoleResultData
					{
						CommandDefinitionRoleId = lane.CommandDefinitionRoleId,
						Name = lane.Name,
						Description = lane.Description,
						LaneType = lane.LaneType,
						SortOrder = lane.SortOrder,
						MinUnitPersonnel = lane.MinUnitPersonnel,
						MaxUnitPersonnel = lane.MaxUnitPersonnel,
						MaxUnits = lane.MaxUnits,
						MinTimeInRole = lane.MinTimeInRole,
						MaxTimeInRole = lane.MaxTimeInRole,
						ForceRequirements = lane.ForceRequirements,
						RequiredUnitTypes = lane.RequiredUnitTypes?.Select(x => x.UnitTypeId).ToList() ?? new List<int>(),
						RequiredPersonnelRoles = lane.RequiredRoles?.Select(x => x.PersonnelRoleId).ToList() ?? new List<int>()
					});
				}
			}

			return data;
		}
		#endregion Private Helpers
	}
}
