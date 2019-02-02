using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Links;
using System.Linq;
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

		public IActionResult Index()
		{
			var model = new LinksIndexView();
			model.DepartmentId = DepartmentId;
			model.Links = _departmentLinksService.GetAllLinksForDepartment(DepartmentId);
			model.CanCreateLinks = _limitsService.CanDepartmentUseLinks(DepartmentId);

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			model.Code = department.LinkCode;

			return View(model);
		}

		[HttpGet]
		public IActionResult New()
		{
			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;

			return View(model);
		}

		[HttpPost]
		public IActionResult New(NewLinksView model)
		{
			if (!_limitsService.CanDepartmentUseLinks(DepartmentId))
			{
				model.Message = $"Unable to a created a link while on the Free Plan.";
				return View(model);
			}
			
			var linkedDepartment = _departmentLinksService.GetDepartmentByLinkCode(model.LinkCode);

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

			if (!_limitsService.CanDepartmentUseLinks(linkedDepartment.DepartmentId))
			{
				model.Message = $"Unable to a link to a department on the Free Plan.";
				return View(model);
			}

			model.Link.DepartmentId = DepartmentId;
			model.Link.LinkedDepartmentId = linkedDepartment.DepartmentId;
			model.Link.LinkEnabled = false;
			model.Link.LinkCreated = DateTime.UtcNow;

			_departmentLinksService.Save(model.Link);
			_emailService.SendNewDepartmentLinkMail(model.Link);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult EnableLink(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			_departmentLinksService.Save(link);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult DisableLink(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.LinkEnabled = false;
			link.LinkAccepted = null;

			_departmentLinksService.Save(link);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult Enable(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;
			model.Link = link;

			return View(model);
		}

		[HttpPost]
		public IActionResult Enable(NewLinksView model)
		{
			var link = _departmentLinksService.GetLinkById(model.Link.DepartmentLinkId);

			if (link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			link.DepartmentColor = model.Link.DepartmentColor;
			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			_departmentLinksService.Save(link);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult DeleteLink(int linkId)
		{


			return RedirectToAction("Index");
		}

		[HttpGet]
		public IActionResult View(int linkId)
		{
			var model = new LinkDataView();
			model.Link = _departmentLinksService.GetLinkById(linkId);

			if (model.Link.DepartmentId != DepartmentId && model.Link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			return View(model);
		}

		[HttpGet]
		public IActionResult GetActiveCallsList(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			List<CallListJson> callsJson = new List<CallListJson>();

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			if (link.LinkEnabled && link.DepartmentShareCalls)
			{
				var calls = _callsService.GetActiveCallsByDepartment(link.DepartmentId).OrderByDescending(x => x.LoggedOn);
				var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

				foreach (var call in calls)
				{
					var callJson = new CallListJson();
					callJson.CallId = call.CallId;
					callJson.Number = call.Number;
					callJson.Name = call.Name;
					callJson.State = _callsService.CallStateToString((CallStates) call.State);
					callJson.StateColor = _callsService.CallStateToColor((CallStates) call.State);
					callJson.Timestamp = call.LoggedOn.TimeConverterToString(department);
					callJson.Priority = _callsService.CallPriorityToString(call.Priority, link.DepartmentId);
					callJson.Color = _callsService.CallPriorityToColor(call.Priority, link.DepartmentId);

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
		public IActionResult GetUnitsList(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = _unitsService.GetUnitsForDepartment(link.DepartmentId);
			var states = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(link.DepartmentId);
			var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

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
					var customState = CustomStatesHelper.GetCustomUnitState(state);

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
		public IActionResult GetPersonnelList(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				Unauthorized();

			var department = _departmentsService.GetDepartmentById(link.DepartmentId);
			var allUsers = _departmentsService.GetAllUsersForDepartment(link.DepartmentId);
			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(link.DepartmentId);
			var departmentGroups = _departmentGroupsService.GetAllGroupsForDepartment(link.DepartmentId);

			var lastUserStates = _userStateService.GetLatestStatesForDepartment(link.DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(link.DepartmentId);

			var calls = _callsService.GetActiveCallsByDepartment(link.DepartmentId);
			var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(link.DepartmentId);

			var names = new Dictionary<string, string>();

			var userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				var state = lastUserStates.FirstOrDefault(x => x.UserId == u.UserId);

				if (state != null)
					userStates.Add(state);
				else
					userStates.Add(_userStateService.GetLastUserStateByUserId(u.UserId));

				var name = personnelNames.FirstOrDefault(x => x.UserId == u.UserId);
				if (name != null)
					names.Add(u.UserId, name.Name);
				else
					names.Add(u.UserId, UserHelper.GetFullNameForUser(u.UserId));
			}

			var personnelViewModels = new List<Models.BigBoardX.PersonnelViewModel>();

			var sortedUngroupedUsers = from u in allUsers
										   // let mu = Membership.GetUser(u.UserId)
									   let userGroup = _departmentGroupsService.GetGroupForUser(u.UserId, DepartmentId)
									   let groupName = userGroup == null ? "" : userGroup.Name
									   let roles = _personnelRolesService.GetRolesForUser(u.UserId, DepartmentId)
									   //let name = (ProfileBase.Create(mu.UserName, true)).GetPropertyValue("Name").ToString()
									   let name = names[u.UserId]
									   let weight = lastUserActionlogs.Where(x => x.UserId == u.UserId).FirstOrDefault().GetWeightForAction()
									   orderby groupName, weight, name ascending
									   select new
									   {
										   Name = name,
										   User = u,
										   Group = userGroup,
										   Roles = roles
									   };

			foreach (var u in sortedUngroupedUsers)
			{
				//var mu = Membership.GetUser(u.User.UserId);
				var al = lastUserActionlogs.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				var us = userStates.Where(x => x.UserId == u.User.UserId).FirstOrDefault();

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
				var personnelViewModel = Models.BigBoardX.PersonnelViewModel.Create(u.Name, al, us, department, respondingToDepartment, u.Group, u.Roles, callNumber);

				personnelViewModels.Add(personnelViewModel);
			}

			return Json(personnelViewModels);
		}
	}
}