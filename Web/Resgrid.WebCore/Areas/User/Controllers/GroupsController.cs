using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Areas.User.Models.Groups;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class GroupsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ILimitsService _limitsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDeleteService _deleteService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUserProfileService _userProfileService;

		public GroupsController(IDepartmentsService departmentsService, IUsersService usersService, IDepartmentGroupsService departmentGroupsService,
			Model.Services.IAuthorizationService authorizationService, ILimitsService limitsService, IGeoLocationProvider geoLocationProvider, IDeleteService deleteService,
			IEventAggregator eventAggregator, IUserProfileService userProfileService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_limitsService = limitsService;
			_geoLocationProvider = geoLocationProvider;
			_deleteService = deleteService;
			_eventAggregator = eventAggregator;
			_userProfileService = userProfileService;
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		public IActionResult Index()
		{
			DepartmentGroupsModel model = new DepartmentGroupsModel();
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartmentUnlimited(DepartmentId);

			model.CanAddNewGroup = _limitsService.CanDepartentAddNewGroup(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Create)]
		public IActionResult NewGroup()
		{
			NewGroupView model = new NewGroupView();
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartmentUnlimited(DepartmentId);


			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));

			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.GenericGroup_Create)]
		public IActionResult NewGroup(NewGroupView model, IFormCollection collection)
		{
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));

			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			var groupAdmins = new List<string>();
			var groupUsers = new List<string>();
			var allUsers = new List<string>();

			if (collection.ContainsKey("groupAdmins"))
				groupAdmins.AddRange(collection["groupAdmins"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("groupUsers"))
				groupUsers.AddRange(collection["groupUsers"].ToString().Split(char.Parse(",")));

			allUsers.AddRange(groupAdmins);
			allUsers.AddRange(groupUsers);

			foreach (var groupUser in allUsers)
			{
				if (_departmentGroupsService.IsUserInAGroup(groupUser, DepartmentId))
				{
					var profile = _userProfileService.GetProfileByUserId(groupUser);

					ModelState.AddModelError("", string.Format("{0} Is already in a group. Cannot add to another.", profile.FullName.AsFirstNameLastName));
				}
			}

			if (model.NewGroup.Type.HasValue && model.NewGroup.Type.Value == (int)DepartmentGroupTypes.Station)
			{
				if (String.IsNullOrWhiteSpace(model.What3Word))
				{
					if (String.IsNullOrEmpty(model.Latitude) && String.IsNullOrEmpty(model.Longitude))
					{
						if (String.IsNullOrEmpty(model.Address1))
							ModelState.AddModelError("Address1", string.Format("The Address field is required for station groups"));

						if (String.IsNullOrEmpty(model.City))
							ModelState.AddModelError("City", string.Format("The City field is required for station groups"));

						if (String.IsNullOrEmpty(model.Country))
							ModelState.AddModelError("Country", string.Format("The Country field is required for station groups"));

						if (String.IsNullOrEmpty(model.PostalCode))
							ModelState.AddModelError("PostalCode", string.Format("The Postal Code field is required for station groups"));

						if (String.IsNullOrEmpty(model.State))
							ModelState.AddModelError("State", string.Format("The State field is required for station groups"));
					}
				}
				else
				{
					var result = _geoLocationProvider.GetCoordinatesFromW3W(model.What3Word);

					if (result == null)
						ModelState.AddModelError("What3Word", string.Format("The What3Words address entered was incorrect."));
					else
					{
						model.Latitude = result.Latitude.ToString();
						model.Longitude = result.Longitude.ToString();
					}

				}

			}

			if (ModelState.IsValid)
			{
				model.NewGroup.DepartmentId = DepartmentId;
				var users = new List<DepartmentGroupMember>();

				foreach (var user in allUsers)
				{
					if (users.All(x => x.UserId != user))
					{
						var dgm = new DepartmentGroupMember();
						dgm.DepartmentId = DepartmentId;
						dgm.UserId = user;

						if (groupAdmins.Contains(user))
							dgm.IsAdmin = true;

						users.Add(dgm);
					}
				}

				if (model.NewGroup.Type.HasValue && model.NewGroup.Type.Value == (int)DepartmentGroupTypes.Station)
				{
					if (String.IsNullOrEmpty(model.Latitude) && String.IsNullOrEmpty(model.Longitude))
					{
						model.NewGroup.Address.Address1 = model.Address1;
						model.NewGroup.Address.City = model.City;
						model.NewGroup.Address.Country = model.Country;
						model.NewGroup.Address.PostalCode = model.PostalCode;
						model.NewGroup.Address.State = model.State;
					}
					else
					{
						model.NewGroup.Address = null;
						model.NewGroup.Latitude = model.Latitude;
						model.NewGroup.Longitude = model.Longitude;
					}

					if (!String.IsNullOrWhiteSpace(model.What3Word))
						model.NewGroup.What3Words = model.What3Word;
				}
				else
				{
					model.NewGroup.Address = null;
				}

				if (model.NewGroup.ParentDepartmentGroupId <= 0)
					model.NewGroup.ParentDepartmentGroupId = null;

				model.NewGroup.Members = users;
				model.NewGroup.DispatchEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);
				model.NewGroup.MessageEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

				_departmentGroupsService.Save(model.NewGroup);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.GroupAdded;
				auditEvent.After = model.NewGroup.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Groups", new { Area = "User" });
			}

			return View("NewGroup", model);
		}

		[Authorize(Policy = ResgridResources.GenericGroup_Delete)]
		public IActionResult DeleteGroup(int departmentGroupId)
		{
			if (!_authorizationService.CanUserEditDepartmentGroup(UserId, departmentGroupId))
				Unauthorized();

			var group = _departmentGroupsService.GetGroupById(departmentGroupId);
			var childGroups = _departmentGroupsService.GetAllChildDepartmentGroups(departmentGroupId);

			if (childGroups != null && childGroups.Count > 0)
			{
				DepartmentGroupsModel model = new DepartmentGroupsModel();
				model.Department = _departmentsService.GetDepartmentByUserId(UserId);
				model.User = _usersService.GetUserById(UserId);
				model.Users = _departmentsService.GetAllUsersForDepartment(model.Department.DepartmentId);
				model.Groups = _departmentGroupsService.GetAllGroupsForDepartmentUnlimited(model.Department.DepartmentId);

				model.CanAddNewGroup = _limitsService.CanDepartentAddNewGroup(model.Department.DepartmentId);

				model.Message = string.Format("Cannot delete the {0} group because it is the parent to other groups.", group.Name);

				return View("Index", model);
			}

			var department = _departmentsService.GetDepartmentById(group.DepartmentId);

			if (department.IsUserAnAdmin(UserId) || group.IsUserGroupAdmin(UserId))
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.GroupRemoved;
				auditEvent.Before = group.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				_deleteService.DeleteGroup(group.DepartmentGroupId);
			}

			return RedirectToAction("Index", "Groups", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public IActionResult EditGroup(int departmentGroupId)
		{
			if (!_authorizationService.CanUserEditDepartmentGroup(UserId, departmentGroupId))
				Unauthorized();

			EditGroupView model = new EditGroupView();
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			model.EditGroup = _departmentGroupsService.GetGroupById(departmentGroupId);
			model.InternalDispatchEmail = $"{model.EditGroup.DispatchEmail}@{Config.InboundEmailConfig.GroupsDomain}";
			model.Latitude = model.EditGroup.Latitude;
			model.Longitude = model.EditGroup.Longitude;
			model.What3Word = model.EditGroup.What3Words;

			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));
			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			if (model.EditGroup.Address != null && model.EditGroup.Type.HasValue && model.EditGroup.Type.Value == (int)DepartmentGroupTypes.Station)
			{
				model.Address1 = model.EditGroup.Address.Address1;
				model.City = model.EditGroup.Address.City;
				model.Country = model.EditGroup.Address.Country;
				model.PostalCode = model.EditGroup.Address.PostalCode;
				model.State = model.EditGroup.Address.State;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public IActionResult EditGroup(EditGroupView model, IFormCollection collection)
		{
			if (!_authorizationService.CanUserEditDepartmentGroup(UserId, model.EditGroup.DepartmentGroupId))
				Unauthorized();

			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));
			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			var group = _departmentGroupsService.GetGroupById(model.EditGroup.DepartmentGroupId);

			if (String.IsNullOrWhiteSpace(group.DispatchEmail))
				group.DispatchEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

			if (String.IsNullOrWhiteSpace(group.MessageEmail))
				group.MessageEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.GroupChanged;
			auditEvent.Before = group.CloneJson();

			group.Name = model.EditGroup.Name;

			var groupAdmins = new List<string>();
			var groupUsers = new List<string>();
			var allUsers = new List<string>();

			if (collection.ContainsKey("groupAdmins"))
				groupAdmins.AddRange(collection["groupAdmins"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("groupUsers"))
				groupUsers.AddRange(collection["groupUsers"].ToString().Split(char.Parse(",")));

			allUsers.AddRange(groupAdmins);
			allUsers.AddRange(groupUsers);

			if (model.EditGroup.Type.HasValue && model.EditGroup.Type.Value == (int)DepartmentGroupTypes.Station)
			{
				if (String.IsNullOrWhiteSpace(model.What3Word))
				{
					if (String.IsNullOrEmpty(model.Latitude) && String.IsNullOrEmpty(model.Longitude))
					{
						if (String.IsNullOrEmpty(model.Address1))
							ModelState.AddModelError("Address1", string.Format("The Address field is required for station groups"));

						if (String.IsNullOrEmpty(model.City))
							ModelState.AddModelError("City", string.Format("The City field is required for station groups"));

						if (String.IsNullOrEmpty(model.Country))
							ModelState.AddModelError("Country", string.Format("The Country field is required for station groups"));

						if (String.IsNullOrEmpty(model.PostalCode))
							ModelState.AddModelError("PostalCode", string.Format("The Postal Code field is required for station groups"));

						if (String.IsNullOrEmpty(model.State))
							ModelState.AddModelError("State", string.Format("The State field is required for station groups"));
					}
				}
				else
				{
					var result = _geoLocationProvider.GetCoordinatesFromW3W(model.What3Word);

					if (result == null)
						ModelState.AddModelError("What3Word", string.Format("The What3Words address entered was incorrect."));
					else
					{
						model.Latitude = result.Latitude.ToString();
						model.Longitude = result.Longitude.ToString();
					}

				}
			}

			if (ModelState.IsValid)
			{
				model.EditGroup.DepartmentId = DepartmentId;
				List<DepartmentGroupMember> users = new List<DepartmentGroupMember>();

				foreach (var user in allUsers)
				{
					if (users.All(x => x.UserId != user))
					{
						var dgm = new DepartmentGroupMember();
						dgm.DepartmentId = DepartmentId;
						dgm.UserId = user;

						if (groupAdmins.Contains(user))
							dgm.IsAdmin = true;

						users.Add(dgm);
					}
				}

				if (model.EditGroup.Type.HasValue && model.EditGroup.Type.Value == (int)DepartmentGroupTypes.Station)
				{
					if (group.Address == null)
						group.Address = new Address();

					if (String.IsNullOrEmpty(model.Latitude) && String.IsNullOrEmpty(model.Longitude))
					{
						group.Address.Address1 = model.Address1;
						group.Address.City = model.City;
						group.Address.Country = model.Country;
						group.Address.PostalCode = model.PostalCode;
						group.Address.State = model.State;
					}
					else
					{
						group.Address = null;
						group.Latitude = model.Latitude;
						group.Longitude = model.Longitude;
					}

					if (!String.IsNullOrWhiteSpace(model.What3Word))
						group.What3Words = model.What3Word;

					group.ParentDepartmentGroupId = null;
					group.Parent = null;
				}
				else
				{
					group.Address = null;
				}

				if (model.EditGroup.ParentDepartmentGroupId <= 0)
					group.ParentDepartmentGroupId = null;

				group.Members = users;
				_departmentGroupsService.Update(group);

				auditEvent.After = group.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Groups", new { Area = "User" });
			}

			model.EditGroup = group;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public IActionResult Geofence(int departmentGroupId)
		{
			var model = new GeofenceView();
			model.Group = _departmentGroupsService.GetGroupById(departmentGroupId);

			if (model.Group.DepartmentId != DepartmentId)
				Unauthorized();

			model.Coordinates = _departmentGroupsService.GetMapCenterCoordinatesForGroup(departmentGroupId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public IActionResult SaveGeofence([FromBody]SaveGeofenceModel model)
		{
			var group = _departmentGroupsService.GetGroupById(model.DepartmentGroupId);

			if (group != null)
			{
				group.GeofenceColor = model.Color;
				group.Geofence = model.GeoFence;

				_departmentGroupsService.Save(group);
				model.Success = true;
				model.Message = "Station response area geofence has been saved.";

				return Json(model);
			}

			return new StatusCodeResult((int)HttpStatusCode.BadRequest);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		
		public IActionResult GetMembersForGroup(int groupId, bool includeAdmins = true, bool includeNormal = true)
		{
			var groupsJson = new List<GroupMemberJson>();
			var groups = _departmentGroupsService.GetAllMembersForGroup(groupId);

			foreach (var group in groups)
			{
				var isAdmin = group.IsAdmin.GetValueOrDefault();

				if ((isAdmin && includeAdmins) || (!isAdmin && includeNormal))
				{
					var groupJson = new GroupMemberJson();
					groupJson.GroupMemberId = group.DepartmentGroupMemberId;
					groupJson.DepartmentGroupId = group.DepartmentGroupId;
					groupJson.UserId = group.UserId;
					groupJson.IsAdmin = isAdmin;
					groupJson.Name = UserHelper.GetFullNameForUser(group.UserId);

					groupsJson.Add(groupJson);
				}
			}

			return Json(groupsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		public IActionResult GetAllGroups()
		{
			var stations = new List<StationJson>();

			var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			foreach (var group in groups)
			{
				var station = new StationJson();
				station.GroupId = group.DepartmentGroupId;
				station.Name = group.Name;

				stations.Add(station);
			}

			return Json(stations);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		
		public IActionResult GetGroupsForCallGrid()
		{
			List<StationJson> groupsJson = new List<StationJson>();
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			foreach (var group in groups)
			{
				StationJson groupJson = new StationJson();
				groupJson.GroupId = group.DepartmentGroupId;
				groupJson.Name = group.Name;

				if (group.Members != null)
					groupJson.Count = group.Members.Count;
				else
					groupJson.Count = 0;

				groupsJson.Add(groupJson);
			}

			return Json(groupsJson);
		}
	}
}
