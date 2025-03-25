using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
	[Area("User")]
	public class CommandController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly ICallsService _callsService;
		private readonly ICommandsService _commandsService;

		public CommandController(ICallsService callsService, ICommandsService commandsService)
		{
			_callsService = callsService;
			_commandsService = commandsService;
		}

		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> Index()
		{
			var model = new CommandIndexView();
			model.Commands = await _commandsService.GetAllCommandsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewCommandView();
			model.Command = new CommandDefinition();
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			var types = new List<CallType>();
			types.Add(new CallType() { Type = "Any Call Type" });
			types.AddRange(model.CallTypes);

			model.Types = new SelectList(types, "CallTypeId", "Type");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public IActionResult New(NewCommandView model, IFormCollection form)
		{
			if (ModelState.IsValid)
			{
				List<int> assignments = (from object key in form.Keys
																 where key.ToString().StartsWith("assignmentName_")
																 select int.Parse(key.ToString().Replace("assignmentName_", ""))).ToList();

				if (assignments.Count > 0)
					model.Command.Assignments = new Collection<CommandDefinitionRole>();

				model.Command.DepartmentId = DepartmentId;

				if (model.SelectedType != 0)
					model.Command.CallTypeId = model.SelectedType;

				//model.Training.CreatedOn = DateTime.UtcNow;
				//model.Training.CreatedByUserId = UserId;
				//model.Training.GroupsToAdd = form["groupsToAdd"];
				//model.Training.RolesToAdd = form["rolesToAdd"];
				//model.Training.UsersToAdd = form["usersToAdd"];
				//model.Training.Description = System.Net.WebUtility.HtmlDecode(model.Training.Description);
				//model.Training.TrainingText = System.Net.WebUtility.HtmlDecode(model.Training.TrainingText);

				foreach (var i in assignments)
				{
					if (form.ContainsKey("assignmentName_" + i))
					{
						var assignmentName = form["assignmentName_" + i];
						var assignmentDescription = form["assignmentDescription_" + i];
						var assignmentLock = bool.Parse(form["assignmentLock_" + i]);

						var assignment = new CommandDefinitionRole();
						assignment.Name = assignmentName;
						assignment.Description = assignmentDescription;
						assignment.ForceRequirements = assignmentLock;


						var units = new List<string>();
						var roles = new List<string>();
						var certs = new List<string>();

						if (form.ContainsKey("assignmentUnits_" + i))
							units.AddRange(form["assignmentUnits_" + i].ToString().Split(char.Parse(",")));

						if (form.ContainsKey("assignmentRoles_" + i))
							roles.AddRange(form["assignmentRoles_" + i].ToString().Split(char.Parse(",")));

						if (form.ContainsKey("assignmentCerts_" + i))
							certs.AddRange(form["assignmentCerts_" + i].ToString().Split(char.Parse(",")));

						foreach (var unitType in units)
						{
							var assignmentUnit = new CommandDefinitionRoleUnitType();
							assignmentUnit.UnitTypeId = int.Parse(unitType);

							assignment.RequiredUnitTypes.Add(assignmentUnit);
						}

						foreach (var personnelRole in roles)
						{
							var assignmentRole = new CommandDefinitionRolePersonnelRole();
							assignmentRole.PersonnelRoleId = int.Parse(personnelRole);

							assignment.RequiredRoles.Add(assignmentRole);
						}

						foreach (var personnelCert in certs)
						{
							var assignmentCert = new CommandDefinitionRoleCert();
							assignmentCert.DepartmentCertificationTypeId = int.Parse(personnelCert);

							//assignment.RequiredCerts.Add(assignmentCert);
						}
					}
				}

				_commandsService.Save(model.Command);

				return RedirectToAction("Index");
			}

			return View(model);
		}
	}
}
