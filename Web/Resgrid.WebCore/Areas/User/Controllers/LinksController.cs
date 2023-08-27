using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Links;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Helpers;
using Resgrid.Web.Areas.User.Models.Units;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class LinksController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailService _emailService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserStateService _userStateService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ILimitsService _limitsService;

		public LinksController(IDepartmentLinksService departmentLinksService, IDepartmentsService departmentsService, IEmailService emailService, 
			ICallsService callsService, IUnitsService unitsService, IActionLogsService actionLogsService, IDepartmentGroupsService departmentGroupsService,
			IUserStateService userStateService, IPersonnelRolesService personnelRolesService, ILimitsService limitsService)
		{
			_departmentLinksService = departmentLinksService;
			_departmentsService = departmentsService;
			_emailService = emailService;
			_callsService = callsService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_userStateService = userStateService;
			_personnelRolesService = personnelRolesService;
			_limitsService = limitsService;
		}
		#endregion Private Members and Constructors

		public async Task<IActionResult> Index()
		{
			var model = new LinksIndexView();
			model.DepartmentId = DepartmentId;
			model.Links = await _departmentLinksService.GetAllLinksForDepartmentAsync(DepartmentId);
			model.CanCreateLinks = await _limitsService.CanDepartmentUseLinksAsync(DepartmentId);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (department != null)
				model.Code = department.LinkCode;

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> New(NewLinksView model, CancellationToken cancellationToken)
		{
			if (!await _limitsService.CanDepartmentUseLinksAsync(DepartmentId))
			{
				model.Message = $"Unable to a created a link while on the Free Plan.";
				return View(model);
			}
			
			var linkedDepartment = await _departmentLinksService.GetDepartmentByLinkCodeAsync(model.LinkCode);

			if (linkedDepartment == null)
			{
				model.Message = $"Unable to find a department with a link code of {model.LinkCode}, please check the code and try again.";
				return View(model);
			}

			if (linkedDepartment.DepartmentId == DepartmentId)
			{
				model.Message = $"Unable to a link to your own department.";
				return View(model);
			}

			if (!await _limitsService.CanDepartmentUseLinksAsync(linkedDepartment.DepartmentId))
			{
				model.Message = $"Unable to a link to a department on the Free Plan.";
				return View(model);
			}

			model.Link.DepartmentId = DepartmentId;
			model.Link.LinkedDepartmentId = linkedDepartment.DepartmentId;
			model.Link.LinkEnabled = false;
			model.Link.LinkCreated = DateTime.UtcNow;

			await _departmentLinksService.SaveAsync(model.Link, cancellationToken);
			await _emailService.SendNewDepartmentLinkMailAsync(model.Link);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> EnableLink(int linkId, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			await _departmentLinksService.SaveAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> DisableLink(int linkId, CancellationToken cancellationToke)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.LinkEnabled = false;
			link.LinkAccepted = null;

			await _departmentLinksService.SaveAsync(link, cancellationToke);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> Enable(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;
			model.Link = link;

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> Enable(NewLinksView model, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(model.Link.DepartmentLinkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.DepartmentColor = model.Link.DepartmentColor;
			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			await _departmentLinksService.SaveAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> DeleteLink(int linkId)
		{


			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> View(int linkId)
		{
			var model = new LinkDataView();
			model.Link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (model.Link.DepartmentId != DepartmentId && model.Link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> GetActiveCallsList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			List<CallListJson> callsJson = new List<CallListJson>();

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			if (link.LinkEnabled && link.DepartmentShareCalls)
			{
				var calls = (await _callsService.GetActiveCallsByDepartmentAsync(link.DepartmentId)).OrderByDescending(x => x.LoggedOn);
				var department = await _departmentsService.GetDepartmentByIdAsync(link.DepartmentId, false);

				foreach (var call in calls)
				{
					var callJson = new CallListJson();
					callJson.CallId = call.CallId;
					callJson.Number = call.Number;
					callJson.Name = call.Name;
					callJson.State = _callsService.CallStateToString((CallStates) call.State);
					callJson.StateColor = _callsService.CallStateToColor((CallStates) call.State);
					callJson.Timestamp = call.LoggedOn.TimeConverterToString(department);
					callJson.Priority = await _callsService.CallPriorityToStringAsync(call.Priority, link.DepartmentId);
					callJson.Color = await _callsService.CallPriorityToColorAsync(call.Priority, link.DepartmentId);

					if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || call.ReportingUserId == UserId)
						callJson.CanDeleteCall = true;
					else
						callJson.CanDeleteCall = false;

					callsJson.Add(callJson);
				}
			}

			return Json(callsJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetUnitsList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(link.DepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(link.DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(link.DepartmentId, false);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetPersonnelList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(link.DepartmentId);
			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(link.DepartmentId);
			var lastUserActionlogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(link.DepartmentId);
			var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(link.DepartmentId);

			var lastUserStates = await _userStateService.GetLatestStatesForDepartmentAsync(link.DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(link.DepartmentId);

			var calls = await _callsService.GetActiveCallsByDepartmentAsync(link.DepartmentId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(link.DepartmentId);

			var names = new Dictionary<string, string>();

			var userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				var state = lastUserStates.FirstOrDefault(x => x.UserId == u.UserId);

				if (state != null)
					userStates.Add(state);
				else
					userStates.Add(await _userStateService.GetLastUserStateByUserIdAsync(u.UserId));

				var name = personnelNames.FirstOrDefault(x => x.UserId == u.UserId);
				if (name != null)
					names.Add(u.UserId, name.Name);
				else
					names.Add(u.UserId, await UserHelper.GetFullNameForUser(u.UserId));
			}

			var personnelViewModels = new List<Models.BigBoardX.PersonnelViewModel>();

			var sortedUngroupedUsers = from u in allUsers
										   // let mu = Membership.GetUser(u.UserId)
									   //let userGroup = await _departmentGroupsService.GetGroupForUserAsync(u.UserId, DepartmentId)
									   //let groupName = userGroup == null ? "" : userGroup.Name
									   //let roles = await _personnelRolesService.GetRolesForUserAsync(u.UserId, DepartmentId)
									   //let name = (ProfileBase.Create(mu.UserName, true)).GetPropertyValue("Name").ToString()
									   let name = names[u.UserId]
									   let weight = lastUserActionlogs.Where(x => x.UserId == u.UserId).FirstOrDefault().GetWeightForAction()
									   orderby weight, name ascending
									   select new
									   {
										   Name = name,
										   User = u,
										   //Group = userGroup,
										   Roles = new List<PersonnelRole>()
									   };



			foreach (var u in sortedUngroupedUsers)
			{
				//var mu = Membership.GetUser(u.User.UserId);
				var al = lastUserActionlogs.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				var us = userStates.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				u.Roles.AddRange(await _personnelRolesService.GetRolesForUserAsync(u.User.UserId, DepartmentId));
				var group = await _departmentGroupsService.GetGroupForUserAsync(u.User.UserId, DepartmentId);

				string callNumber = "";
				if (al != null && al.ActionTypeId == (int)ActionTypes.RespondingToScene || (al != null && al.DestinationType.HasValue && al.DestinationType.Value == 2))
				{
					if (al.DestinationId.HasValue)
					{
						var call = calls.FirstOrDefault(x => x.CallId == al.DestinationId.Value);

						if (call != null)
							callNumber = call.Number;
					}
				}
				var respondingToDepartment = stations.Where(s => al != null && s.DepartmentGroupId == al.DestinationId).FirstOrDefault();
				var personnelViewModel = await Models.BigBoardX.PersonnelViewModel.Create(u.Name, al, us, department, respondingToDepartment, group, u.Roles, callNumber);

				personnelViewModels.Add(personnelViewModel);
			}

			return Json(personnelViewModels);
		}
	}
}
