using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
using Newtonsoft.Json;

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
		private readonly IUnitsService _unitsService;
		private readonly IShiftsService _shiftsService;

		public GroupsController(IDepartmentsService departmentsService, IUsersService usersService, IDepartmentGroupsService departmentGroupsService,
			Model.Services.IAuthorizationService authorizationService, ILimitsService limitsService, IGeoLocationProvider geoLocationProvider, IDeleteService deleteService,
			IEventAggregator eventAggregator, IUserProfileService userProfileService, IUnitsService unitsService, IShiftsService shiftsService)
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
			_unitsService = unitsService;
			_shiftsService = shiftsService;
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		public async Task<IActionResult> Index()
		{
			DepartmentGroupsModel model = new DepartmentGroupsModel();
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId);

			model.CanAddNewGroup = await _limitsService.CanDepartmentAddNewGroup(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Create)]
		public async Task<IActionResult> NewGroup()
		{
			NewGroupView model = new NewGroupView();
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId);


			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			//groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));
			groups.AddRange(model.Groups.Where(x => x.ParentDepartmentGroupId.HasValue == false));

			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.GenericGroup_Create)]
		public async Task<IActionResult> NewGroup(NewGroupView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

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
				if (await _departmentGroupsService.IsUserInAGroupAsync(groupUser, DepartmentId))
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(groupUser);

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
					var result = await _geoLocationProvider.GetCoordinatesFromW3W(model.What3Word);

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
						model.NewGroup.Address = new Address();
						model.NewGroup.Address.Address1 = model.Address1;
						model.NewGroup.Address.City = model.City;
						model.NewGroup.Address.Country = model.Country;
						model.NewGroup.Address.PostalCode = model.PostalCode;
						model.NewGroup.Address.State = model.State;
					}
					else
					{
						model.NewGroup.Address = null;
						model.NewGroup.Latitude = StringHelpers.SanitizeCoordinatesString(model.Latitude);
						model.NewGroup.Longitude = StringHelpers.SanitizeCoordinatesString(model.Longitude);
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

				if (!String.IsNullOrWhiteSpace(model.PrinterApiKey) && !String.IsNullOrWhiteSpace(model.PrinterId))
				{
					var printer = new DepartmentGroupPrinter();
					printer.PrinterId = int.Parse(model.PrinterId);
					printer.PrinterName = model.PrinterName;
					printer.ApiKey = SymmetricEncryption.Encrypt(model.PrinterApiKey, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

					model.NewGroup.DispatchToPrinter = true;
					model.NewGroup.PrinterData = JsonConvert.SerializeObject(printer);
				}

				model.NewGroup.Members = users;
				model.NewGroup.DispatchEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);
				model.NewGroup.MessageEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

				if (!String.IsNullOrWhiteSpace(model.NewGroup.Latitude))
					model.NewGroup.Latitude = StringHelpers.SanitizeCoordinatesString(model.NewGroup.Latitude.Replace(" ", ""));

				if (!String.IsNullOrWhiteSpace(model.NewGroup.Longitude))
					model.NewGroup.Longitude = StringHelpers.SanitizeCoordinatesString(model.NewGroup.Longitude.Replace(" ", ""));

				await _departmentGroupsService.SaveAsync(model.NewGroup, cancellationToken);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.GroupAdded;
				auditEvent.After = model.NewGroup.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Groups", new { Area = "User" });
			}

			return View("NewGroup", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Delete)]
		public async Task<IActionResult> DeleteGroup(int departmentGroupId)
		{
			DeleteGroupView model = new DeleteGroupView();
			model.Group = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId, true);

			if (model.Group == null || model.Group.DepartmentId != DepartmentId || !await _authorizationService.CanUserEditDepartmentGroupAsync(UserId, departmentGroupId))
				Unauthorized();

			var users = _departmentGroupsService.GetAllUsersForGroup(departmentGroupId);
			var childGroups = await _departmentGroupsService.GetAllChildDepartmentGroupsAsync(departmentGroupId);
			var units = await _unitsService.GetAllUnitsForGroupAsync(departmentGroupId);
			var shifts = await _shiftsService.GetShiftGroupsByGroupIdAsync(departmentGroupId);

			if (users != null)
				model.UserCount = users.Count;
			else
				model.UserCount = 0;

			if (childGroups != null)
				model.ChildGroupCount = childGroups.Count;
			else
				model.ChildGroupCount = 0;

			if (units != null)
				model.UnitsCount = units.Count;
			else
				model.UnitsCount = 0;

			if (shifts != null)
				model.ShiftsCount = shifts.Count;
			else
				model.ShiftsCount = 0;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.GenericGroup_Delete)]
		public async Task<IActionResult> DeleteGroup(DeleteGroupView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditDepartmentGroupAsync(UserId, model.Group.DepartmentGroupId))
				Unauthorized();

			var group = await _departmentGroupsService.GetGroupByIdAsync(model.Group.DepartmentGroupId, true);

			if (group == null)
				return RedirectToAction("Index", "Groups", new { Area = "User" });

			if (group.DepartmentId != DepartmentId)
				Unauthorized();

			var users = _departmentGroupsService.GetAllUsersForGroup(model.Group.DepartmentGroupId);
			var childGroups = await _departmentGroupsService.GetAllChildDepartmentGroupsAsync(model.Group.DepartmentGroupId);
			var units = await _unitsService.GetAllUnitsForGroupAsync(model.Group.DepartmentGroupId);
			var shifts = await _shiftsService.GetShiftGroupsByGroupIdAsync(model.Group.DepartmentGroupId);

			if (childGroups.Count > 0 || users.Count > 0 || units.Count > 0 || shifts.Count > 0)
			{
				model.Group = group;

				if (users != null)
					model.UserCount = users.Count;
				else
					model.UserCount = 0;

				if (childGroups != null)
					model.ChildGroupCount = childGroups.Count;
				else
					model.ChildGroupCount = 0;

				if (units != null)
					model.UnitsCount = units.Count;
				else
					model.UnitsCount = 0;

				if (shifts != null)
					model.ShiftsCount = shifts.Count;
				else
					model.ShiftsCount = 0;

				model.Message = string.Format("Cannot delete the {0} group because it is the parent to other groups, has users or units in it or has shifts assigned to it.", model.Group.Name);

				return View(model);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(group.DepartmentId);

			if (department.IsUserAnAdmin(UserId) || group.IsUserGroupAdmin(UserId))
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.GroupRemoved;
				auditEvent.Before = group.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _deleteService.DeleteGroupAsync(group.DepartmentGroupId, DepartmentId, UserId, cancellationToken);
			}

			return RedirectToAction("Index", "Groups", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public async Task<IActionResult> EditGroup(int departmentGroupId)
		{
			if (!await _authorizationService.CanUserEditDepartmentGroupAsync(UserId, departmentGroupId))
				Unauthorized();

			EditGroupView model = new EditGroupView();
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.EditGroup = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId);
			model.InternalDispatchEmail = $"{model.EditGroup.DispatchEmail}@{Config.InboundEmailConfig.GroupsDomain}";
			model.Latitude = StringHelpers.SanitizeCoordinatesString(model.EditGroup.Latitude);
			model.Longitude = StringHelpers.SanitizeCoordinatesString(model.EditGroup.Longitude);
			model.What3Word = model.EditGroup.What3Words;

			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.ParentDepartmentGroupId.HasValue == false && x.DepartmentGroupId != departmentGroupId));
			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			if (model.EditGroup.Address != null && model.EditGroup.Type.HasValue && model.EditGroup.Type.Value == (int)DepartmentGroupTypes.Station)
			{
				model.Address1 = model.EditGroup.Address.Address1;
				model.City = model.EditGroup.Address.City;
				model.Country = model.EditGroup.Address.Country;
				model.PostalCode = model.EditGroup.Address.PostalCode;
				model.State = model.EditGroup.Address.State;
			}

			if (!String.IsNullOrWhiteSpace(model.EditGroup.PrinterData))
			{
				var printer = JsonConvert.DeserializeObject<DepartmentGroupPrinter>(model.EditGroup.PrinterData);

				model.PrinterId = printer.PrinterId.ToString();
				model.PrinterName = printer.PrinterName;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public async Task<IActionResult> EditGroup(EditGroupView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditDepartmentGroupAsync(UserId, model.EditGroup.DepartmentGroupId))
				Unauthorized();

			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			List<DepartmentGroup> groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup { DepartmentGroupId = -1, Name = "None" });
			groups.AddRange(model.Groups.Where(x => x.Type.HasValue && x.Type.Value == (int)DepartmentGroupTypes.Station));
			model.StationGroups = new SelectList(groups, "DepartmentGroupId", "Name");

			var group = await _departmentGroupsService.GetGroupByIdAsync(model.EditGroup.DepartmentGroupId);

			if (String.IsNullOrWhiteSpace(group.DispatchEmail))
				group.DispatchEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

			if (String.IsNullOrWhiteSpace(group.MessageEmail))
				group.MessageEmail = RandomGenerator.GenerateRandomString(6, 6, false, true, false, true, true, false, null);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.GroupChanged;
			auditEvent.Before = group.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

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
					var result = await _geoLocationProvider.GetCoordinatesFromW3W(model.What3Word);

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

				foreach (var user in allUsers)
				{
					if (group.Members.All(x => x.UserId != user))
					{
						var dgm = new DepartmentGroupMember();
						dgm.DepartmentId = DepartmentId;
						dgm.UserId = user;

						if (groupAdmins.Contains(user))
							dgm.IsAdmin = true;
						else
							dgm.IsAdmin = false;

						group.Members.Add(dgm);
					}
				}

				if (allUsers.Count > 0)
				{
					var usersToRemove = group.Members.Where(x => !allUsers.Contains(x.UserId)).ToList();
					foreach (var user in usersToRemove)
					{
						group.Members.Remove(user);
					}
				}
				else
				{
					group.Members.Clear();
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
						group.Latitude = StringHelpers.SanitizeCoordinatesString(model.Latitude);
						group.Longitude = StringHelpers.SanitizeCoordinatesString(model.Longitude);
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
				else
					group.ParentDepartmentGroupId = model.EditGroup.ParentDepartmentGroupId;

				if (!String.IsNullOrWhiteSpace(model.PrinterApiKey) && !String.IsNullOrWhiteSpace(model.PrinterId))
				{
					var printer = new DepartmentGroupPrinter();
					printer.PrinterId = int.Parse(model.PrinterId);
					printer.PrinterName = model.PrinterName;
					printer.ApiKey = SymmetricEncryption.Encrypt(model.PrinterApiKey, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

					group.PrinterData = JsonConvert.SerializeObject(printer);
				}
				group.DispatchToPrinter = model.EditGroup.DispatchToPrinter;

				if (!String.IsNullOrWhiteSpace(group.Latitude))
					group.Latitude = StringHelpers.SanitizeCoordinatesString(group.Latitude.Replace(" ", ""));

				if (!String.IsNullOrWhiteSpace(group.Longitude))
					group.Longitude = StringHelpers.SanitizeCoordinatesString(group.Longitude.Replace(" ", ""));

				await _departmentGroupsService.UpdateAsync(group, cancellationToken);

				auditEvent.After = group.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index", "Groups", new { Area = "User" });
			}

			model.EditGroup = group;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public async Task<IActionResult> Geofence(int departmentGroupId)
		{
			var model = new GeofenceView();
			model.Group = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId, true);

			if (model.Group == null)
				Unauthorized();

			if (model.Group.DepartmentId != DepartmentId)
				Unauthorized();

			model.Coordinates = await _departmentGroupsService.GetMapCenterCoordinatesForGroupAsync(departmentGroupId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.GenericGroup_Update)]
		public async Task<IActionResult> SaveGeofence([FromBody]SaveGeofenceModel model, CancellationToken cancellationToken)
		{
			var group = await _departmentGroupsService.GetGroupByIdAsync(model.DepartmentGroupId);

			if (group != null)
			{
				group.GeofenceColor = model.Color;
				group.Geofence = model.GeoFence;

				await _departmentGroupsService.SaveAsync(group, cancellationToken);
				model.Success = true;
				model.Message = "Station response area geofence has been saved.";

				return Json(model);
			}

			return new StatusCodeResult((int)HttpStatusCode.BadRequest);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		
		public async Task<IActionResult> GetMembersForGroup(int groupId, bool includeAdmins = true, bool includeNormal = true)
		{
			var groupsJson = new List<GroupMemberJson>();
			var groups = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);

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
					groupJson.Name = await UserHelper.GetFullNameForUser(group.UserId);

					groupsJson.Add(groupJson);
				}
			}

			return Json(groupsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.GenericGroup_View)]
		public async Task<IActionResult> GetAllGroups()
		{
			var stations = new List<StationJson>();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

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
		
		public async Task<IActionResult> GetGroupsForCallGrid()
		{
			List<StationJson> groupsJson = new List<StationJson>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

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
