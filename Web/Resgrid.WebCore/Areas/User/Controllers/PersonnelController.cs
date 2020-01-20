using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Resgrid.Web.Areas.User.Models.Profile;
using Resgrid.WebCore.Areas.User.Models.Personnel;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class PersonnelController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly ILimitsService _limitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEmailService _emailService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IDeleteService _deleteService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserStateService _userStateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IEmailMarketingProvider _emailMarketingProvider;
		private readonly ICertificationService _certificationService;
		private readonly ICustomStateService _customStateService;
		private readonly IGeoService _geoService;
		private readonly UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> _userManager;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public PersonnelController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IUserProfileService userProfileService, IDeleteService deleteService, Model.Services.IAuthorizationService authorizationService,
			ILimitsService limitsService, IPersonnelRolesService personnelRolesService, IDepartmentGroupsService departmentGroupsService, IUserStateService userStateService,
			IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider, ICertificationService certificationService, ICustomStateService customStateService,
			IGeoService geoService, UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> userManager, IDepartmentSettingsService departmentSettingsService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_emailService = emailService;
			_userProfileService = userProfileService;
			_deleteService = deleteService;
			_authorizationService = authorizationService;
			_limitsService = limitsService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_userStateService = userStateService;
			_eventAggregator = eventAggregator;
			_emailMarketingProvider = emailMarketingProvider;
			_certificationService = certificationService;
			_customStateService = customStateService;
			_geoService = geoService;
			_userManager = userManager;
			_departmentSettingsService = departmentSettingsService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Personnel_View)]
		public IActionResult Index()
		{
			PersonnelModel model = new PersonnelModel();
			model.LastActivityDates = new Dictionary<Guid, string>();
			model.States = new Dictionary<Guid, string>();
			model.Groups = new Dictionary<Guid, DepartmentGroup>();

			model.CanAddNewUser = _limitsService.CanDepartentAddNewUser(DepartmentId);
			model.CanGroupAdminsAdd = _authorizationService.CanGroupAdminsAddUsers(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_Create)]
		public IActionResult AddPerson()
		{
			if (!_authorizationService.CanUserAddNewUser(DepartmentId, UserId))
				Unauthorized();

			var model = new AddPersonModel();
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.Profile = new UserProfile();
			model.SendAccountCreationNotification = true;

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(_departmentGroupsService.GetAllGroupsForDepartment(DepartmentId));

			var isUserGroupAdmin = _departmentGroupsService.IsUserAGroupAdmin(UserId, DepartmentId);
			if (isUserGroupAdmin && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
				model.Groups = new SelectList(groups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId), "DepartmentGroupId", "Name");
			}
			else
			{
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			}

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && isUserGroupAdmin)
			{
				model.GroupAdmin = true;
				var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);

				model.UserGroup = group.DepartmentGroupId;
				model.GroupName = group.Name;
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public IActionResult ViewPerson(string userId)
		{
			if (!_authorizationService.CanUserViewUser(UserId, userId))
				Unauthorized();

			ViewPersonView model = new ViewPersonView();
			model.Profile = _userProfileService.GetProfileByUserId(userId, true);
			model.User = _usersService.GetUserById(userId);

			var member = _departmentsService.GetDepartmentMember(userId, DepartmentId);
			if (member != null)
				model.Department = _departmentsService.GetDepartmentById(member.DepartmentId);
			else
				model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			model.Group = _departmentGroupsService.GetGroupForUser(userId, DepartmentId);
			var roles = _personnelRolesService.GetRolesForUser(userId, DepartmentId);

			if (roles != null && roles.Count > 0)
			{
				foreach (var role in roles)
				{
					if (string.IsNullOrWhiteSpace(model.Roles))
						model.Roles = role.Name;
					else
						model.Roles += string.Format(", {0}", role.Name);
				}
			}
			else
			{
				model.Roles = "None";
			}

			StringBuilder sb = new StringBuilder();
			if (member != null)
			{
				if (member.IsAdmin.HasValue && member.IsAdmin.Value ||
						model.Department.ManagingUserId == userId)
					sb.Append("Admin");
				else
					sb.Append("Normal");

				if (member.IsDisabled.HasValue && member.IsDisabled.Value)
					sb.Append(sb.Length > 0 ? ", Disabled" : "Disabled");

				if (member.IsHidden.HasValue && member.IsHidden.Value)
					sb.Append(sb.Length > 0 ? ", Hidden" : "Hidden");

				model.State = sb.ToString();
			}

			model.UserState = _userStateService.GetLastUserStateByUserId(userId);
			model.ActionLog = _actionLogsService.GetLastActionLogForUser(userId, DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Personnel_Create)]
		public async Task<IActionResult> AddPerson(AddPersonModel model, IFormCollection form)
		{
			if (!_authorizationService.CanUserAddNewUser(DepartmentId, UserId))
				Unauthorized();

			ModelState.Remove("Profile.UserId");

			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(UserId);

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(_departmentGroupsService.GetAllGroupsForDepartment(DepartmentId));

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");

			model.IsUserGroupAdmin = _departmentGroupsService.IsUserAGroupAdmin(UserId, DepartmentId);
			if (model.IsUserGroupAdmin)
			{
				var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
				model.Groups = new SelectList(groups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId), "DepartmentGroupId", "Name");
			}
			else
			{
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			}

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && model.IsUserGroupAdmin)
			{
				model.GroupAdmin = true;
				var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);

				model.UserGroup = group.DepartmentGroupId;
				model.GroupName = group.Name;
			}

			if (!String.IsNullOrEmpty(model.Profile.MobileNumber))
			{
				if (model.Carrier == MobileCarriers.None)
					ModelState.AddModelError("Carrier", "If you entered a mobile phone, you need to select your mobile carrier. If you carrier is not listed select one and contact us to have your carrier added.");
				else
				{
					if (model.Carrier == MobileCarriers.VirginMobileUk && !model.Profile.MobileNumber.StartsWith("0"))
						ModelState.AddModelError("Profile.MobileNumber", "Virgin Mobile Uk requires your phone number to start with 0.");

					if (model.Carrier == MobileCarriers.O2 && !model.Profile.MobileNumber.StartsWith("44"))
						ModelState.AddModelError("Profile.MobileNumber", "O2 requires your phone number to start with 44.");

					if (model.Carrier == MobileCarriers.Orange && !model.Profile.MobileNumber.StartsWith("0"))
						ModelState.AddModelError("Profile.MobileNumber", "Orange requires your phone number to start with 0.");

					if (model.Carrier == MobileCarriers.TMobileUk && !model.Profile.MobileNumber.StartsWith("0"))
						ModelState.AddModelError("Profile.MobileNumber", "T-Mobile Uk requires your phone number to start with 0.");

					if (model.Carrier == MobileCarriers.Vodafone && !model.Profile.MobileNumber.StartsWith("0"))
						ModelState.AddModelError("Profile.MobileNumber", "Vodafone requires your phone number to start with 0.");
				}
			}

			if ((model.Profile.SendSms || model.Profile.SendMessageSms) && String.IsNullOrEmpty(model.Profile.MobileNumber))
			{
				ModelState.AddModelError("Profile.MobileNumber", "You have selected you want SMS/Text notifications but have not supplied a mobile number.");
			}

			var deletedUserId = _departmentsService.GetUserIdForDeletedUserInDepartment(DepartmentId, model.Email);
			if (deletedUserId != null)
			{
				return RedirectToAction("ReactivateUser", "Personnel", new { area = "User", id = deletedUserId });
			}

			var existingEmailAddress = _usersService.GetUserByEmail(model.Email);
			if (existingEmailAddress != null)
			{
				return RedirectToAction("AddExistingUser", "Personnel", new { area = "User", id = existingEmailAddress.Id });
			}

			var existingUsername = _usersService.GetUserByName(model.Username);
			if (existingUsername != null)
			{
				ModelState.AddModelError("Username", $"The username {model.Username} has already been taken, please try another.");
			}

			if (ModelState.IsValid)
			{
				var user = new Microsoft.AspNet.Identity.EntityFramework6.IdentityUser { UserName = model.Username, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
				var result = await _userManager.CreateAsync(user, model.NewPassword);
				if (result.Succeeded)
				{
					model.Profile.UserId = user.UserId;
					model.Profile.MobileCarrier = (int)model.Carrier;
					model.Profile.FirstName = model.FirstName;
					model.Profile.LastName = model.LastName;
					_userProfileService.SaveProfile(DepartmentId, model.Profile);

					_usersService.AddUserToUserRole(user.Id);

					// Don't know why this is stil erroring out.
					try
					{
						_usersService.InitUserExtInfo(user.Id);
					}
					catch { }

					//_departmentsService.AddUserToDepartment(model.Department.Name, user.UserId);
					_departmentsService.AddUserToDepartment(DepartmentId, user.UserId);

					var userObject = _usersService.GetUserById(user.UserId);

					_eventAggregator.SendMessage<UserCreatedEvent>(new UserCreatedEvent() { DepartmentId = DepartmentId, Name = string.Format("{0} {1}", model.FirstName, model.LastName), User = userObject });

					var auditEvent = new AuditEvent();
					auditEvent.DepartmentId = DepartmentId;
					auditEvent.UserId = UserId;
					auditEvent.Type = AuditLogTypes.UserAdded;
					auditEvent.After = userObject;
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					if (model.UserGroup != 0)
						_departmentGroupsService.MoveUserIntoGroup(user.UserId, model.UserGroup, model.IsUserGroupAdmin, DepartmentId);

					if (form.ContainsKey("roles"))
					{
						var roles = form["roles"].ToString().Split(char.Parse(","));

						if (roles.Any())
							_personnelRolesService.SetRolesForUser(DepartmentId, user.UserId, roles);
					}
					
					_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
					_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
					_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();
					_usersService.ClearCacheForDepartment(DepartmentId);

					if (model.SendAccountCreationNotification)
						_emailService.SendWelcomeEmail(model.Department.Name, model.FirstName + " " + model.LastName, user.Email, user.UserName, model.ConfirmPassword, DepartmentId);

					_emailMarketingProvider.SubscribeUserToUsersList(model.FirstName, model.LastName, user.Email);

					return RedirectToAction("Index", "Personnel", new { area = "User" });
				}
				else
				{
					ModelState.AddModelError("", "Unable to create user, please check the form and try again");
				}
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_Delete)]
		public IActionResult DeletePerson(string userId)
		{
			if (!_authorizationService.CanUserDeleteUser(DepartmentId, UserId, userId))
				Unauthorized();

			DeletePersonModel model = new DeletePersonModel();
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(userId);
			model.UserId = userId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Personnel_Delete)]
		public IActionResult DeletePerson(DeletePersonModel model)
		{
			if (!_authorizationService.CanUserDeleteUser(DepartmentId, UserId, model.UserId))
				Unauthorized();

			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(model.UserId);

			if (model.Department.ManagingUserId == model.UserId)
				ModelState.AddModelError("", "Cannot delete the Managing User");

			if (ModelState.IsValid)
			{
				if (model.AreYouSure)
				{
					var member = _departmentsService.DeleteUser(DepartmentId, model.UserId, UserId);
					//var result = _deleteService.DeleteUser(DepartmentId, UserId, model.UserId);

					_userProfileService.ClearUserProfileFromCache(model.UserId);
					_userProfileService.ClearAllUserProfilesFromCache(model.Department.DepartmentId);
					_departmentsService.InvalidateDepartmentUsersInCache(model.Department.DepartmentId);
					_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();
					_usersService.ClearCacheForDepartment(DepartmentId);

					_eventAggregator.SendMessage<DepartmentSettingsChangedEvent>(new DepartmentSettingsChangedEvent() { DepartmentId = DepartmentId });

					if (member != null && member.IsDeleted)
					{
						return RedirectToAction("Index", "Personnel", new { area = "User" });
					}

					ModelState.AddModelError("", "Error while trying to delete this person, please try again latter.");
				}
				else
				{
					ModelState.AddModelError("AreYouSure", "You need to confirm the delete.");
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelForGrid()
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			//var dep = _departmentsService.GetDepartmentByUserId(UserId);
			//var users = _departmentsService.GetAllUsersForDepartment(DepartmentId);//.GetAllUsersForDepartmentUnlimitedMinusDisabled(DepartmentId);
			//var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			var personGroupRoles = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, false, false, false);

			foreach (var user in personGroupRoles)
			{
				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				//person.Name = UserHelper.GetFullNameForUser(personnelNames, null, user.UserId);
				person.Name = user.Name;

				//var group = _departmentGroupsService.GetGroupForUser(user.UserId);
				//if (group != null)
				//	person.Group = group.Name;
				person.Group = user.DepartmentGroupName;

				//var roles = _personnelRolesService.GetRolesForUser(user.UserId);
				person.Roles = new List<string>();
				foreach (var role in user.RoleNamesList)
				{
					person.Roles.Add(role);
				}

				personnelJson.Add(person);
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelForCallGrid(string callLat, string callLong)
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			var users = _departmentsService.GetAllUsersForDepartment(DepartmentId);//.GetAllUsersForDepartmentUnlimitedMinusDisabled(DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			var userStates = _userStateService.GetLatestStatesForDepartment(DepartmentId);

			foreach (var user in users)
			{
				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				person.Name = UserHelper.GetFullNameForUser(personnelNames, user.UserName, user.UserId);

				var group = _departmentGroupsService.GetGroupForUser(user.UserId, DepartmentId);
				if (group != null)
					person.Group = group.Name;

				var roles = _personnelRolesService.GetRolesForUser(user.UserId, DepartmentId);
				person.Roles = new List<string>();
				foreach (var role in roles)
				{
					person.Roles.Add(role.Name);
				}

				var currentStaffing = userStates.FirstOrDefault(x => x.UserId == user.UserId);
				if (currentStaffing != null)
				{
					var staffing = CustomStatesHelper.GetCustomPersonnelStaffing(DepartmentId, currentStaffing);

					if (staffing != null)
					{
						person.Staffing = staffing.ButtonText;
						person.StaffingColor = staffing.ButtonClassToColor();
					}
				}
				else
				{
					person.Staffing = "Available";
					person.StaffingColor = "#000";
				}

				var currentStatus = lastUserActionlogs.FirstOrDefault(x => x.UserId == user.UserId);
				if (currentStatus != null)
				{
					var status = CustomStatesHelper.GetCustomPersonnelStatus(DepartmentId, currentStatus);
					if (status != null)
					{
						person.Status = status.ButtonText;
						person.StatusColor = status.ButtonClassToColor();
					}
				}
				else
				{
					person.Status = "Standing By";
					person.StatusColor = "#000";
				}

				if (String.IsNullOrWhiteSpace(callLat) || String.IsNullOrWhiteSpace(callLong) || currentStatus == null || String.IsNullOrWhiteSpace(currentStatus.GeoLocationData))
					person.Eta = "N/A";
				else
				{
					var eta = _geoService.GetEtaInSeconds(currentStatus.GeoLocationData, String.Format("{0},{1}", callLat, callLong));

					if (eta > 0)
						person.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
					else
						person.Eta = "N/A";
				}

				personnelJson.Add(person);
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelForGridWithFilter(bool filterSelf = false)
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			var dep = _departmentsService.GetDepartmentByUserId(UserId);
			var users = _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabled(dep.DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			foreach (var user in users)
			{
				if (filterSelf)
					if (user.UserId == UserId)
						continue;

				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				person.Name = UserHelper.GetFullNameForUser(personnelNames, user.UserName, user.UserId);

				var group = _departmentGroupsService.GetGroupForUser(user.UserId, DepartmentId);
				if (group != null)
					person.Group = group.Name;

				var roles = _personnelRolesService.GetRolesForUser(user.UserId, DepartmentId);
				person.Roles = new List<string>();
				foreach (var role in roles)
				{
					person.Roles.Add(role.Name);
				}

				personnelJson.Add(person);
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelList()
		{
			List<PersonnelForListJson> personnelJson = new List<PersonnelForListJson>();
			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var users = _departmentsService.GetAllUsersForDepartmentUnlimited(DepartmentId);
			var departmentMembers = _departmentsService.GetAllMembersForDepartmentUnlimited(DepartmentId);
			//var actionLogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId, true);
			//var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);
			var canGroupAdminsDelete = _authorizationService.CanGroupAdminsRemoveUsers(DepartmentId);
			var profiles = _userProfileService.GetAllProfilesForDepartmentIncDisabledDeleted(DepartmentId);
			var userGroupRoles = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, true, true, false);

			var sortOrder = _departmentSettingsService.GetDepartmentPersonnelSortOrder(DepartmentId);

			foreach (var user in users)
			{
				var person = new PersonnelForListJson();
				person.UserId = user.UserId.ToString();

				var member = departmentMembers.FirstOrDefault(x => x.UserId == user.UserId);
				//var actionLog = actionLogs.FirstOrDefault(x => x.UserId == user.UserId);
				//var userProfile = _userProfileService.GetProfileByUserId(user.UserId);

				if (!profiles.ContainsKey(user.UserId))
				{
					person.Name = "Unknown User";
				}
				else
				{
					var userProfile = profiles[user.UserId];
					person.Name = userProfile.FullName.AsFirstNameLastName;
					person.FirstName = userProfile.FirstName;
					person.LastName = userProfile.LastName;
				}

				if (ClaimsAuthorizationHelper.CanViewPII())
					person.EmailAddress = user.Email;
				else
					person.EmailAddress = "";

				var group = _departmentGroupsService.GetGroupForUser(user.UserId, DepartmentId);
				if (group != null)
				{
					person.Group = group.Name;

					if (department.IsUserAnAdmin(UserId) || (canGroupAdminsDelete && group.IsUserGroupAdmin(UserId) && !department.IsUserAnAdmin(user.UserId)))
						person.CanRemoveUser = true;

					if (group.IsUserGroupAdmin(UserId) || department.IsUserAnAdmin(UserId))
						person.CanEditUser = true;
				}
				else
				{
					if (department.IsUserAnAdmin(UserId))
					{
						person.CanRemoveUser = true;
						person.CanEditUser = true;
					}
				}

				//var roles = _personnelRolesService.GetRolesForUser(user.UserId);
				//foreach (var role in roles)
				//{
				//	if (String.IsNullOrWhiteSpace(person.Roles))
				//		person.Roles = role.Name;
				//	else
				//		person.Roles += ", " + role.Name;
				//}

				var userGroupRole = userGroupRoles.FirstOrDefault(x => x.UserId == user.UserId);
				if (userGroupRole != null)
					person.Roles = userGroupRole.RoleNames;
				else
					person.Roles = "";

				StringBuilder sb = new StringBuilder();

				if (member != null)
				{
					if (member.IsAdmin.HasValue && member.IsAdmin.Value ||
							department.ManagingUserId == user.UserId)
						sb.Append("Admin");
					else
						sb.Append("Normal");

					if (member.IsDisabled.HasValue && member.IsDisabled.Value)
						sb.Append(sb.Length > 0 ? ", Disabled" : "Disabled");

					if (member.IsHidden.HasValue && member.IsHidden.Value)
						sb.Append(sb.Length > 0 ? ", Hidden" : "Hidden");

					person.State = sb.ToString();
					//person.EmailAddress = membership.Email;
				}
				else
				{
					person.State = "Normal";
				}

				//if (actionLog != null)
				//{
				//	person.LastActivityDate = actionLog.Timestamp.TimeConverterToString(department);
				//}
				//else
				//{
				//	person.LastActivityDate = "Never";
				//}

				personnelJson.Add(person);
			}

			switch (sortOrder)
			{
				case PersonnelSortOrders.Default:
					return Json(personnelJson);
				case PersonnelSortOrders.FirstName:
					return Json(personnelJson.OrderBy(x => x.FirstName));
				case PersonnelSortOrders.LastName:
					return Json(personnelJson.OrderBy(x => x.LastName));
				default:
					return Json(personnelJson);
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelListPaged(int perPage, int page)
		{
			PersonnelListPagedResult result = new PersonnelListPagedResult();
			result.Data = new List<PersonnelForListJson>();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var users = _departmentsService.GetAllUsersForDepartmentUnlimited(DepartmentId);
			var departmentMembers = _departmentsService.GetAllMembersForDepartmentUnlimited(DepartmentId);
			//var actionLogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId, true);
			//var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);
			var canGroupAdminsDelete = _authorizationService.CanGroupAdminsRemoveUsers(DepartmentId);
			var profiles = _userProfileService.GetAllProfilesForDepartmentIncDisabledDeleted(DepartmentId);
			var userGroupRoles = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, true, true, false);

			var sortOrder = _departmentSettingsService.GetDepartmentPersonnelSortOrder(DepartmentId);

			foreach (var user in users)
			{
				var person = new PersonnelForListJson();
				person.UserId = user.UserId.ToString();

				var member = departmentMembers.FirstOrDefault(x => x.UserId == user.UserId);
				//var actionLog = actionLogs.FirstOrDefault(x => x.UserId == user.UserId);
				//var userProfile = _userProfileService.GetProfileByUserId(user.UserId);

				if (!profiles.ContainsKey(user.UserId))
				{
					person.Name = "Unknown User";
				}
				else
				{
					var userProfile = profiles[user.UserId];
					person.Name = userProfile.FullName.AsFirstNameLastName;
					person.FirstName = userProfile.FirstName;
					person.LastName = userProfile.LastName;
				}

				if (ClaimsAuthorizationHelper.CanViewPII())
					person.EmailAddress = user.Email;
				else
					person.EmailAddress = "";

				var group = _departmentGroupsService.GetGroupForUser(user.UserId, DepartmentId);
				if (group != null)
				{
					person.Group = group.Name;

					if (department.IsUserAnAdmin(UserId) || (canGroupAdminsDelete && group.IsUserGroupAdmin(UserId) && !department.IsUserAnAdmin(user.UserId)))
						person.CanRemoveUser = true;

					if (group.IsUserGroupAdmin(UserId) || department.IsUserAnAdmin(UserId))
						person.CanEditUser = true;
				}
				else
				{
					if (department.IsUserAnAdmin(UserId))
					{
						person.CanRemoveUser = true;
						person.CanEditUser = true;
					}
				}

				//var roles = _personnelRolesService.GetRolesForUser(user.UserId);
				//foreach (var role in roles)
				//{
				//	if (String.IsNullOrWhiteSpace(person.Roles))
				//		person.Roles = role.Name;
				//	else
				//		person.Roles += ", " + role.Name;
				//}

				var userGroupRole = userGroupRoles.FirstOrDefault(x => x.UserId == user.UserId);
				if (userGroupRole != null)
					person.Roles = userGroupRole.RoleNames;
				else
					person.Roles = "";

				StringBuilder sb = new StringBuilder();

				if (member != null)
				{
					if (member.IsAdmin.HasValue && member.IsAdmin.Value ||
							department.ManagingUserId == user.UserId)
						sb.Append("Admin");
					else
						sb.Append("Normal");

					if (member.IsDisabled.HasValue && member.IsDisabled.Value)
						sb.Append(sb.Length > 0 ? ", Disabled" : "Disabled");

					if (member.IsHidden.HasValue && member.IsHidden.Value)
						sb.Append(sb.Length > 0 ? ", Hidden" : "Hidden");

					person.State = sb.ToString();
					//person.EmailAddress = membership.Email;
				}
				else
				{
					person.State = "Normal";
				}

				//if (actionLog != null)
				//{
				//	person.LastActivityDate = actionLog.Timestamp.TimeConverterToString(department);
				//}
				//else
				//{
				//	person.LastActivityDate = "Never";
				//}

				result.Data.Add(person);
			}

			switch (sortOrder)
			{
				case PersonnelSortOrders.FirstName:
					result.Data = result.Data.OrderBy(x => x.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					result.Data = result.Data.OrderBy(x => x.LastName).ToList();
					break;
			}

			result.Total = result.Data.Count;
			result.Page = page;
			result.Data = result.Data.Skip(perPage * (page - 1))
									 .Take(perPage).ToList();

			return Json(result);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelGroupStatusForGridCombined()
		{
			List<GroupStatusJson> personnelGroupJson = new List<GroupStatusJson>();
			var dep = _departmentsService.GetDepartmentByUserId(UserId);
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(dep.DepartmentId);
			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(dep.DepartmentId);
			var allUsers = _departmentsService.GetAllUsersForDepartment(dep.DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			List<UserState> userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				if (!_departmentsService.IsUserDisabled(u.UserId, DepartmentId) && !_departmentsService.IsUserHidden(u.UserId, DepartmentId))
				{
					userStates.Add(_userStateService.GetLastUserStateByUserId(u.UserId));
				}
			}

			foreach (var departmentGroup in groups)
			{
				GroupStatusJson group = new GroupStatusJson();
				group.GroupId = departmentGroup.DepartmentGroupId;
				group.Name = departmentGroup.Name;
				group.Personnel = new List<PersonnelStatusJson>();

				var sortedUsers = from u in departmentGroup.Members
													let name = UserHelper.GetFullNameForUser(personnelNames, u.User.UserName, u.UserId)
													orderby name ascending
													select new
													{
														Name = name,
														User = u
													};

				foreach (var member in sortedUsers)
				{
					PersonnelStatusJson person = new PersonnelStatusJson();
					person.UserId = member.User.UserId;
					person.Name = member.Name;

					var al = lastUserActionlogs.Where(x => x.UserId == member.User.UserId).FirstOrDefault();

					if (al != null)
					{
						person.Status = al.GetActionText();
						person.StatusCss = al.GetActionCss();
						person.LastActionDate = al.Timestamp;
					}

					var level = userStates.Where(x => x.UserId == member.User.UserId).FirstOrDefault();

					if (level != null)
					{
						person.Staffing = level.GetStaffingText();
						person.StaffingCss = level.GetStaffingCss();
					}

					group.Personnel.Add(person);
				}

				personnelGroupJson.Add(group);
			}


			return Json(personnelGroupJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelGroupStatusForGrid()
		{
			List<GroupStatusJson> personnelGroupJson = new List<GroupStatusJson>();
			var dep = _departmentsService.GetDepartmentByUserId(UserId);
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(dep.DepartmentId);

			foreach (var departmentGroup in groups)
			{
				GroupStatusJson group = new GroupStatusJson();
				group.GroupId = departmentGroup.DepartmentGroupId;
				group.Name = departmentGroup.Name;
				group.CanSetGroupStatus = false || (dep.IsUserAnAdmin(UserId) || departmentGroup.IsUserGroupAdmin(UserId));

				personnelGroupJson.Add(group);
			}

			GroupStatusJson defaultGroup = new GroupStatusJson();
			defaultGroup.GroupId = 0;
			defaultGroup.Name = "";
			defaultGroup.CanSetGroupStatus = false;

			personnelGroupJson.Add(defaultGroup);

			return Json(personnelGroupJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetPersonnelGroupMemberStatusForGrid(int groupId)
		{
			List<PersonnelStatusJson> personnel = new List<PersonnelStatusJson>();
			var dep = _departmentsService.GetDepartmentByUserId(UserId);
			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(dep.DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			if (groupId == 0)
			{
				var allUsers = _departmentsService.GetAllUsersForDepartment(dep.DepartmentId);
				var groupedUserIds = _departmentGroupsService.AllGroupedUserIdsForDepartment(dep.DepartmentId);

				var unGroupedUsers = from u in allUsers
														 where !(from uid in groupedUserIds
																		 select uid).Contains(u.UserId)
														 select u;

				var sortedUsers = from u in unGroupedUsers
													let name = UserHelper.GetFullNameForUser(personnelNames, u.UserName, u.UserId)
													orderby name ascending
													select new
													{
														Name = name,
														User = u
													};

				foreach (var member in sortedUsers)
				{
					PersonnelStatusJson person = new PersonnelStatusJson();
					person.UserId = member.User.UserId;
					person.Name = member.Name;

					var al = lastUserActionlogs.Where(x => x.UserId == member.User.UserId).FirstOrDefault();

					if (al != null)
					{
						person.Status = al.GetActionText();
						person.StatusCss = al.GetActionCss();
						person.LastActionDate = al.Timestamp;
					}

					var level = _userStateService.GetLastUserStateByUserId(member.User.UserId);

					if (level != null)
					{
						person.Staffing = level.GetStaffingText();
						person.StaffingCss = level.GetStaffingCss();
					}

					personnel.Add(person);
				}
			}
			else
			{
				var depGroup = _departmentGroupsService.GetGroupById(groupId);

				var sortedUsers = from u in depGroup.Members
													let name = UserHelper.GetFullNameForUser(personnelNames, u.User.UserName, u.UserId)
													orderby name ascending
													select new
													{
														Name = name,
														User = u
													};

				foreach (var member in sortedUsers)
				{
					PersonnelStatusJson person = new PersonnelStatusJson();
					person.UserId = member.User.UserId;
					person.Name = member.Name;

					var al = lastUserActionlogs.Where(x => x.UserId == member.User.UserId).FirstOrDefault();

					if (al != null)
					{
						person.Status = al.GetActionText();
						person.StatusCss = al.GetActionCss();
						person.LastActionDate = al.Timestamp;
					}

					var level = _userStateService.GetLastUserStateByUserId(member.User.UserId);

					if (level != null)
					{
						person.Staffing = level.GetStaffingText();
						person.StaffingCss = level.GetStaffingCss();
					}

					personnel.Add(person);
				}
			}


			return Json(personnel);
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetMembersForRole(int id)
		{
			var role = _personnelRolesService.GetRoleById(id);

			return Json(role.Users.Select(x => x.UserId).ToList());
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult ReactivateUser(string id)
		{
			ViewPersonView model = new ViewPersonView();
			model.Profile = _userProfileService.GetProfileByUserId(id, true);
			model.User = _usersService.GetUserById(id);

			var member = _departmentsService.GetDepartmentMember(id, DepartmentId);
			if (member != null)
				model.Department = _departmentsService.GetDepartmentById(member.DepartmentId);
			else
				model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			model.Group = _departmentGroupsService.GetGroupForUser(id, DepartmentId);
			var roles = _personnelRolesService.GetRolesForUser(id, DepartmentId);

			if (roles != null && roles.Count > 0)
			{
				foreach (var role in roles)
				{
					if (string.IsNullOrWhiteSpace(model.Roles))
						model.Roles = role.Name;
					else
						model.Roles += string.Format(", {0}", role.Name);
				}
			}
			else
			{
				model.Roles = "None";
			}

			StringBuilder sb = new StringBuilder();
			if (member != null)
			{
				if (member.IsAdmin.HasValue && member.IsAdmin.Value ||
						model.Department.ManagingUserId == id)
					sb.Append("Admin");
				else
					sb.Append("Normal");

				if (member.IsDisabled.HasValue && member.IsDisabled.Value)
					sb.Append(sb.Length > 0 ? ", Disabled" : "Disabled");

				if (member.IsHidden.HasValue && member.IsHidden.Value)
					sb.Append(sb.Length > 0 ? ", Hidden" : "Hidden");

				model.State = sb.ToString();
			}

			_departmentsService.ReactivateUser(DepartmentId, id);

			_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
			_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
			_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
			_departmentsService.InvalidateDepartmentMembers();
			_usersService.ClearCacheForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public IActionResult AddExistingUser(string id)
		{
			ViewPersonView model = new ViewPersonView();
			model.Profile = _userProfileService.GetProfileByUserId(id, true);
			model.User = _usersService.GetUserById(id);

			var member = _departmentsService.GetDepartmentMember(id, DepartmentId);
			if (member != null)
				model.Department = _departmentsService.GetDepartmentById(member.DepartmentId);
			else
				model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			model.Group = _departmentGroupsService.GetGroupForUser(id, DepartmentId);
			var roles = _personnelRolesService.GetRolesForUser(id, DepartmentId);

			if (roles != null && roles.Count > 0)
			{
				foreach (var role in roles)
				{
					if (string.IsNullOrWhiteSpace(model.Roles))
						model.Roles = role.Name;
					else
						model.Roles += string.Format(", {0}", role.Name);
				}
			}
			else
			{
				model.Roles = "None";
			}

			StringBuilder sb = new StringBuilder();
			if (member != null)
			{
				if (member.IsAdmin.HasValue && member.IsAdmin.Value ||
						model.Department.ManagingUserId == id)
					sb.Append("Admin");
				else
					sb.Append("Normal");

				if (member.IsDisabled.HasValue && member.IsDisabled.Value)
					sb.Append(sb.Length > 0 ? ", Disabled" : "Disabled");

				if (member.IsHidden.HasValue && member.IsHidden.Value)
					sb.Append(sb.Length > 0 ? ", Hidden" : "Hidden");

				model.State = sb.ToString();
			}

			_departmentsService.AddExistingUser(DepartmentId, id);

			_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
			_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
			_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
			_departmentsService.InvalidateDepartmentMembers();
			_usersService.ClearCacheForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public IActionResult GetRolesForCallGrid()
		{
			List<RoleForJson> rolesJson = new List<RoleForJson>();
			var roles = _personnelRolesService.GetRolesForDepartment(DepartmentId);

			foreach (var role in roles)
			{
				RoleForJson roleJson = new RoleForJson();
				roleJson.RoleId = role.PersonnelRoleId;
				roleJson.Name = role.Name;

				if (role.Users != null)
					roleJson.Count = role.Users.Count;
				else
					roleJson.Count = 0;

				rolesJson.Add(roleJson);
			}

			return Json(rolesJson);
		}

		#region Roles
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		[Authorize(Policy = ResgridResources.Role_View)]
		public IActionResult Roles()
		{
			PersonnelRolesModel model = new PersonnelRolesModel();
			model.Roles = _personnelRolesService.GetRolesForDepartment(DepartmentId);
			model.CanAddNewRole = _limitsService.CanDepartentAddNewRole(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_Create)]
		public IActionResult AddRole()
		{
			AddRoleModel model = new AddRoleModel();
			model.Role = new PersonnelRole();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Role_Create)]
		public IActionResult AddRole(AddRoleModel model)
		{
			model.Role.DepartmentId = DepartmentId;

			if (_personnelRolesService.GetRoleByDepartmentAndName(model.Role.DepartmentId, model.Role.Name) != null)
				ModelState.AddModelError("Role.Name", "Role with that name already exists in the department.");

			if (ModelState.IsValid)
			{
				_personnelRolesService.SaveRole(model.Role);

				return RedirectToAction("Roles");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_Update)]
		public IActionResult EditRole(int roleId)
		{
			if (!_authorizationService.CanUserEditRole(UserId, roleId))
				Unauthorized();

			EditRoleModel model = new EditRoleModel();
			model.Role = _personnelRolesService.GetRoleById(roleId);
			model.Users = _departmentsService.GetAllUsersForDepartment(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Role_Update)]
		public IActionResult EditRole(EditRoleModel model, IFormCollection collection)
		{
			if (!_authorizationService.CanUserEditRole(UserId, model.Role.PersonnelRoleId))
				Unauthorized();

			var role = _personnelRolesService.GetRoleById(model.Role.PersonnelRoleId);
			role.Name = model.Role.Name;
			role.Description = model.Role.Description;

			if (_personnelRolesService.GetRoleByDepartmentAndName(model.Role.DepartmentId, model.Role.Name) != null)
				ModelState.AddModelError("Role.Name", "Role with that name already exists in the department.");

			if (ModelState.IsValid)
			{
				//using (var scope = new TransactionScope())
				//{
				_personnelRolesService.DeleteRoleUsers(role.Users.ToList());

				if (collection.ContainsKey("users"))
				{
					var users = collection["users"].ToString().Split(char.Parse(","));

					if (users.Any())
					{
						foreach (var user in users)
						{
							PersonnelRoleUser pru = new PersonnelRoleUser();
							string userId = user;
							pru.UserId = userId;

							role.Users.Add(pru);
						}
					}
				}

				//	scope.Complete();
				//}

				_personnelRolesService.SaveRole(role);

				//_userProfileService.ClearUserProfileFromCache(model.UserId);
				_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
				_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
				_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
				_departmentsService.InvalidateDepartmentMembers();
				_usersService.ClearCacheForDepartment(DepartmentId);

				return RedirectToAction("Roles");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_Delete)]
		public IActionResult DeleteRole(int roleId)
		{
			if (!_authorizationService.CanUserEditRole(UserId, roleId))
				Unauthorized();

			_personnelRolesService.DeleteRoleById(roleId);

			return RedirectToAction("Roles");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_View)]
		public IActionResult ViewRole(int roleId)
		{
			if (!_authorizationService.CanUserViewRole(UserId, roleId))
				Unauthorized();

			ViewRoleModel model = new ViewRoleModel();
			model.Role = _personnelRolesService.GetRoleById(roleId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]

		public IActionResult GetRoles()
		{
			var rolesJson = new List<RoleForJson>();
			var roles = _personnelRolesService.GetRolesForDepartment(DepartmentId);

			foreach (var role in roles)
			{
				var roleJson = new RoleForJson();
				roleJson.RoleId = role.PersonnelRoleId;
				roleJson.Name = role.Name;

				rolesJson.Add(roleJson);
			}

			return Json(rolesJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]

		public IActionResult GetCertifications()
		{
			var certificationsJson = new List<CertificationJson>();
			var certifications = _certificationService.GetAllCertificationTypesByDepartment(DepartmentId);

			foreach (var certification in certifications)
			{
				var certificationJson = new CertificationJson();
				certificationJson.Id = certification.DepartmentCertificationTypeId;
				certificationJson.Name = certification.Type;

				certificationsJson.Add(certificationJson);
			}

			return Json(certificationsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]

		public IActionResult GetRolesForUser(string userId)
		{
			var rolesJson = new List<RoleForJson>();
			var roles = _personnelRolesService.GetRolesForUser(userId, DepartmentId);

			foreach (var role in roles)
			{
				var roleJson = new RoleForJson();
				roleJson.RoleId = role.PersonnelRoleId;
				roleJson.Name = role.Name;

				rolesJson.Add(roleJson);
			}

			return Json(rolesJson);
		}
		#endregion Roles
	}
}
