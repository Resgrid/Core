using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Command;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Command definition templates: the department's pre-configured incident command boards. Each
	/// definition is keyed to a call type (or "Any Call Type") and its assignments/lanes seed the live
	/// command board when command is established on a call of that type.
	/// </summary>
	[Area("User")]
	public class CommandController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly ICallsService _callsService;
		private readonly ICommandsService _commandsService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public CommandController(ICallsService callsService, ICommandsService commandsService,
			IUnitsService unitsService, IPersonnelRolesService personnelRolesService)
		{
			_callsService = callsService;
			_commandsService = commandsService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
		}

		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> Index()
		{
			var model = new CommandIndexView();
			model.Commands = await _commandsService.GetAllCommandsForDepartmentAsync(DepartmentId);
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewCommandView();
			model.Command = new CommandDefinition();

			await PopulateSupportingDataAsync(model);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<IActionResult> New(NewCommandView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(model.Command?.Name))
				ModelState.AddModelError("Command.Name", "Command name is required.");

			if (ModelState.IsValid)
			{
				model.Command.DepartmentId = DepartmentId;
				model.Command.CallTypeId = model.SelectedType != 0 ? model.SelectedType : (int?)null;
				model.Command.Assignments = ParseAssignmentsFromForm(form);

				await _commandsService.Save(model.Command, cancellationToken);

				return RedirectToAction("Index");
			}

			await PopulateSupportingDataAsync(model);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<IActionResult> Edit(int commandId)
		{
			var command = await _commandsService.GetCommandByIdAsync(commandId);

			if (command == null || command.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new NewCommandView();
			model.Command = command;
			model.SelectedType = command.CallTypeId ?? 0;

			await PopulateSupportingDataAsync(model);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<IActionResult> Edit(NewCommandView model, IFormCollection form, CancellationToken cancellationToken)
		{
			var command = await _commandsService.GetCommandByIdAsync(model.Command.CommandDefinitionId);

			if (command == null || command.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			if (string.IsNullOrWhiteSpace(model.Command?.Name))
				ModelState.AddModelError("Command.Name", "Command name is required.");

			if (ModelState.IsValid)
			{
				command.Name = model.Command.Name;
				command.Description = model.Command.Description;
				command.CallTypeId = model.SelectedType != 0 ? model.SelectedType : (int?)null;
				command.Timer = model.Command.Timer;
				command.TimerMinutes = model.Command.TimerMinutes;
				command.Assignments = ParseAssignmentsFromForm(form);

				await _commandsService.Save(command, cancellationToken);

				return RedirectToAction("Index");
			}

			model.Command = command;
			await PopulateSupportingDataAsync(model);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> View(int commandId)
		{
			var command = await _commandsService.GetCommandByIdAsync(commandId);

			if (command == null || command.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new NewCommandView();
			model.Command = command;
			model.SelectedType = command.CallTypeId ?? 0;
			await PopulateSupportingDataAsync(model);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_Delete)]
		public async Task<IActionResult> Delete(int commandId, CancellationToken cancellationToken)
		{
			var command = await _commandsService.GetCommandByIdAsync(commandId);

			if (command != null && command.DepartmentId == DepartmentId)
				await _commandsService.DeleteAsync(command, cancellationToken);

			return RedirectToAction("Index");
		}

		#region Private Helpers

		private async Task PopulateSupportingDataAsync(NewCommandView model)
		{
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			var types = new List<CallType>();
			types.Add(new CallType() { Type = "Any Call Type" });
			if (model.CallTypes != null)
				types.AddRange(model.CallTypes);

			model.Types = new SelectList(types, "CallTypeId", "Type", model.SelectedType);

			model.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>();
			model.PersonnelRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();
		}

		/// <summary>
		/// Parses the dynamic assignment/lane rows out of the posted form. Rows are indexed form fields
		/// (assignmentName_N, assignmentDescription_N, ...); SortOrder is assigned from the row order so
		/// the board seeds lanes in the order they appear in the editor.
		/// </summary>
		private static List<CommandDefinitionRole> ParseAssignmentsFromForm(IFormCollection form)
		{
			var assignments = new List<CommandDefinitionRole>();

			var indexes = form.Keys
				.Where(k => k.StartsWith("assignmentName_", StringComparison.OrdinalIgnoreCase))
				.Select(k => int.TryParse(k.Substring("assignmentName_".Length), out var i) ? i : (int?)null)
				.Where(i => i.HasValue)
				.Select(i => i.Value)
				.OrderBy(i => i)
				.ToList();

			int sortOrder = 0;
			foreach (var i in indexes)
			{
				string name = form[$"assignmentName_{i}"];
				if (string.IsNullOrWhiteSpace(name))
					continue;

				var assignment = new CommandDefinitionRole();
				assignment.Name = name.Trim();
				assignment.Description = form[$"assignmentDescription_{i}"];
				assignment.SortOrder = sortOrder++;

				if (int.TryParse(form[$"assignmentId_{i}"], out var roleId))
					assignment.CommandDefinitionRoleId = roleId;

				if (int.TryParse(form[$"assignmentLaneType_{i}"], out var laneType) && Enum.IsDefined(typeof(CommandNodeType), laneType))
					assignment.LaneType = laneType;

				if (bool.TryParse(form[$"assignmentLock_{i}"], out var forceRequirements))
					assignment.ForceRequirements = forceRequirements;

				// The form is a full document: absent/empty pickers clear the lane's requirements.
				assignment.RequiredUnitTypes = ParseIdCsv(form[$"assignmentUnitTypes_{i}"])
					.Select(id => new CommandDefinitionRoleUnitType { UnitTypeId = id }).ToList();
				assignment.RequiredRoles = ParseIdCsv(form[$"assignmentRoles_{i}"])
					.Select(id => new CommandDefinitionRolePersonnelRole { PersonnelRoleId = id }).ToList();

				assignments.Add(assignment);
			}

			return assignments;
		}

		private static List<int> ParseIdCsv(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return new List<int>();

			return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(x => int.TryParse(x, out var id) ? id : 0)
				.Where(id => id > 0)
				.Distinct()
				.ToList();
		}

		#endregion Private Helpers
	}
}
