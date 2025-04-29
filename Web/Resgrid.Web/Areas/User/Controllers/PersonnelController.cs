using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
using Resgrid.Model.Identity;
using Resgrid.WebCore.Areas.User.Models.Personnel;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Resgrid.WebCore.Areas.User.Models;
using System.Web;

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
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICallsService _callsService;

		public PersonnelController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IUserProfileService userProfileService, IDeleteService deleteService, Model.Services.IAuthorizationService authorizationService,
			ILimitsService limitsService, IPersonnelRolesService personnelRolesService, IDepartmentGroupsService departmentGroupsService, IUserStateService userStateService,
			IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider, ICertificationService certificationService, ICustomStateService customStateService,
			IGeoService geoService, UserManager<IdentityUser> userManager, IDepartmentSettingsService departmentSettingsService, ICallsService callsService)
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
			_callsService = callsService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> Index()
		{
			PersonnelModel model = new PersonnelModel();
			model.LastActivityDates = new Dictionary<Guid, string>();
			model.States = new Dictionary<Guid, string>();
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId); //new Dictionary<Guid, DepartmentGroup>();

			model.CanAddNewUser = await _limitsService.CanDepartmentAddNewUserAsync(DepartmentId);
			model.CanGroupAdminsAdd = await _authorizationService.CanGroupAdminsAddUsersAsync(DepartmentId);

			var personnelStates = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			var personnelStaffing = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);

			if (personnelStates != null)
			{
				model.PersonnelCustomStatusesId = personnelStates.CustomStateId;
				model.PersonnelStates = personnelStates.Details.ToList();
			}
			else
				model.PersonnelStates = _customStateService.GetDefaultPersonStatuses();

			if (personnelStaffing != null)
			{
				model.PersonnelCustomStaffingId = personnelStaffing.CustomStateId;
				model.PersonnelStaffings = personnelStaffing.Details.ToList();
			}
			else
				model.PersonnelStaffings = _customStateService.GetDefaultPersonStaffings();

			List<PersonnelForListJson> personnelJson = new List<PersonnelForListJson>();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedAsync(DepartmentId);
			var departmentMembers = await _departmentsService.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId);
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var staffings = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			var canGroupAdminsDelete = await _authorizationService.CanGroupAdminsRemoveUsersAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId);
			var userGroupRoles = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, false);

			var sortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);

			foreach (var user in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
					continue;

				var person = new PersonnelForListJson();
				person.UserId = user.UserId.ToString();

				var member = departmentMembers.FirstOrDefault(x => x.UserId == user.UserId);
				var actionLog = actionLogs.FirstOrDefault(x => x.UserId == user.UserId);
				var staffing = staffings.FirstOrDefault(x => x.UserId == user.UserId);

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

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				if (group != null)
				{
					person.Group = group.Name;

					if (department.IsUserAnAdmin(UserId) || (canGroupAdminsDelete && group.IsUserGroupAdmin(UserId) && !department.IsUserAnAdmin(user.UserId)))
						person.CanRemoveUser = true;

					if (group.IsUserGroupAdmin(UserId) || department.IsUserAnAdmin(UserId))
						person.CanEditUser = true;

					person.GroupId = group.DepartmentGroupId;
				}
				else
				{
					if (department.IsUserAnAdmin(UserId))
					{
						person.CanRemoveUser = true;
						person.CanEditUser = true;
					}
				}

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
				}
				else
				{
					person.State = "Normal";
				}

				if (actionLog != null)
				{
					person.StatusId = actionLog.ActionTypeId;
				}

				if (staffing != null)
				{
					person.StaffingId = staffing.State;
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
					model.Persons = personnelJson;
					break;
				case PersonnelSortOrders.FirstName:
					model.Persons = personnelJson.OrderBy(x => x.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					model.Persons = personnelJson.OrderBy(x => x.LastName).ToList();
					break;
				case PersonnelSortOrders.Group:
					model.Persons = personnelJson.OrderBy(x => x.GroupId).ToList();
					break;
				default:
					model.Persons = personnelJson;
					break;
			}

			List<BSTreeModel> trees = new List<BSTreeModel>();
			var tree0 = new BSTreeModel();
			tree0.id = "TreeGroup_-1";
			tree0.text = "All Personnel";
			tree0.icon = "";
			trees.Add(tree0);

			var tree1 = new BSTreeModel();
			tree1.id = "TreeGroup_0";
			tree1.text = "Ungrouped Personnel";
			tree1.icon = "";
			trees.Add(tree1);

			if (model.Groups != null && model.Groups.Any())
			{
				foreach (var topLevelGroup in model.Groups.Where(x => !x.ParentDepartmentGroupId.HasValue).ToList())
				{
					var group = new BSTreeModel();
					group.id = $"TreeGroup_{topLevelGroup.DepartmentGroupId.ToString()}";
					group.text = topLevelGroup.Name;
					group.icon = "";

					if (topLevelGroup.Children != null && topLevelGroup.Children.Any())
					{
						foreach (var secondLevelGroup in topLevelGroup.Children)
						{
							var secondLevelGroupTree = new BSTreeModel();
							secondLevelGroupTree.id = $"TreeGroup_{secondLevelGroup.DepartmentGroupId.ToString()}";
							secondLevelGroupTree.text = secondLevelGroup.Name;
							secondLevelGroupTree.icon = "";

							group.nodes.Add(secondLevelGroupTree);
						}
					}

					trees.Add(group);
				}
			}
			model.TreeData = Newtonsoft.Json.JsonConvert.SerializeObject(trees);


			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_Create)]
		public async Task<IActionResult> AddPerson()
		{
			if (!await _authorizationService.CanUserAddNewUserAsync(DepartmentId, UserId))
				Unauthorized();

			var model = new AddPersonModel();
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
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
			groups.AddRange(await  _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId));

			var isUserGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
			if (isUserGroupAdmin && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				model.Groups = new SelectList(groups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId), "DepartmentGroupId", "Name");
			}
			else
			{
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			}

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && isUserGroupAdmin)
			{
				model.GroupAdmin = true;
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);

				model.UserGroup = group.DepartmentGroupId;
				model.GroupName = group.Name;
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> ViewPerson(string userId)
		{
			if (!await _authorizationService.CanUserViewUserAsync(UserId, userId))
				Unauthorized();

			ViewPersonView model = new ViewPersonView();
			model.Profile = await _userProfileService.GetProfileByUserIdAsync(userId, true);
			model.User = _usersService.GetUserById(userId);

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);
			if (member != null)
				model.Department = await _departmentsService.GetDepartmentByIdAsync(member.DepartmentId);
			else
				model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Group = await _departmentGroupsService.GetGroupForUserAsync(userId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, DepartmentId);

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

			model.UserState = await _userStateService.GetLastUserStateByUserIdAsync(userId);
			model.ActionLog = await _actionLogsService.GetLastActionLogForUserAsync(userId, DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Personnel_Create)]
		public async Task<IActionResult> AddPerson(AddPersonModel model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddNewUserAsync(DepartmentId, UserId))
				Unauthorized();

			ModelState.Remove("Profile.UserId");

			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId));

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");

			model.IsUserGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
			if (model.IsUserGroupAdmin)
			{
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				model.Groups = new SelectList(groups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId), "DepartmentGroupId", "Name");
			}
			else
			{
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			}

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && model.IsUserGroupAdmin)
			{
				model.GroupAdmin = true;
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);

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

			var deletedUserId = await _departmentsService.GetUserIdForDeletedUserInDepartmentAsync(DepartmentId, model.Email);
			if (deletedUserId != null)
			{
				return RedirectToAction("ReactivateUser", "Personnel", new { area = "User", id = deletedUserId });
			}

			var existingEmailAddress = _usersService.GetUserByEmail(model.Email);
			if (existingEmailAddress != null)
			{
				return RedirectToAction("AddExistingUser", "Personnel", new { area = "User", id = existingEmailAddress.Id });
			}

			var existingUsername = await _usersService.GetUserByNameAsync(model.Username);
			if (existingUsername != null)
			{
				ModelState.AddModelError("Username", $"The username {model.Username} has already been taken, please try another.");
			}

			if (ModelState.IsValid)
			{
				var user = new IdentityUser { UserName = model.Username, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString().ToUpper() };
				var result = await _userManager.CreateAsync(user, model.NewPassword);
				if (result.Succeeded)
				{
					model.Profile.UserId = user.UserId;
					model.Profile.MobileCarrier = (int)model.Carrier;
					model.Profile.FirstName = model.FirstName;
					model.Profile.LastName = model.LastName;
					await _userProfileService.SaveProfileAsync(DepartmentId, model.Profile, cancellationToken);

					_usersService.AddUserToUserRole(user.Id);

					// Don't know why this is still erroring out.
					try
					{
						_usersService.InitUserExtInfo(user.Id);
					}
					catch { }

					await _departmentsService.AddUserToDepartmentAsync(DepartmentId, user.UserId, false, cancellationToken);

					var userObject = _usersService.GetUserById(user.UserId);

					_eventAggregator.SendMessage<UserCreatedEvent>(new UserCreatedEvent() { DepartmentId = DepartmentId, Name = string.Format("{0} {1}", model.FirstName, model.LastName), User = userObject });

					var auditEvent = new AuditEvent();
					auditEvent.DepartmentId = DepartmentId;
					auditEvent.UserId = UserId;
					auditEvent.Type = AuditLogTypes.UserAdded;
					auditEvent.After = userObject.CloneJsonToString();
					auditEvent.Successful = true;
					auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
					auditEvent.ServerName = Environment.MachineName;
					auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					if (model.UserGroup != 0)
						await _departmentGroupsService.MoveUserIntoGroupAsync(user.UserId, model.UserGroup, model.IsUserGroupAdmin, DepartmentId, cancellationToken);

					if (form.ContainsKey("roles"))
					{
						var roles = form["roles"].ToString().Split(char.Parse(","));

						if (roles.Any())
							await _personnelRolesService.SetRolesForUserAsync(DepartmentId, user.UserId, roles, cancellationToken);
					}

					_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
					_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
					_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();
					_usersService.ClearCacheForDepartment(DepartmentId);

					if (model.SendAccountCreationNotification)
						await _emailService.SendWelcomeEmail(model.Department.Name, model.FirstName + " " + model.LastName, user.Email, user.UserName, model.ConfirmPassword, DepartmentId);

					await _emailMarketingProvider.SubscribeUserToUsersList(model.FirstName, model.LastName, user.Email);

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
		public async Task<IActionResult> DeletePerson(string userId)
		{
			if (!await _authorizationService.CanUserDeleteUserAsync(DepartmentId, UserId, userId))
				Unauthorized();

			DeletePersonModel model = new DeletePersonModel();
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(userId);
			model.UserId = userId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Personnel_Delete)]
		public async Task<IActionResult> DeletePerson(DeletePersonModel model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserDeleteUserAsync(DepartmentId, UserId, model.UserId))
				Unauthorized();

			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(model.UserId);

			if (model.Department.ManagingUserId == model.UserId)
				ModelState.AddModelError("", "Cannot delete the Managing User");

			if (ModelState.IsValid)
			{
				if (model.AreYouSure)
				{
					var member = await _departmentsService.DeleteUserAsync(DepartmentId, model.UserId, UserId, cancellationToken);
					//var result = await _deleteService.DeleteUser(DepartmentId, UserId, model.UserId);

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
		public async Task<IActionResult> GetPersonnelForGrid()
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			//var dep = await _departmentsService.GetDepartmentByUserId(UserId);
			//var users = await _departmentsService.GetAllUsersForDepartment(DepartmentId);//.GetAllUsersForDepartmentUnlimitedMinusDisabled(DepartmentId);
			//var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			var personGroupRoles = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);

			foreach (var user in personGroupRoles)
			{
				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				//person.Name = UserHelper.GetFullNameForUser(personnelNames, null, user.UserId);
				person.Name = user.Name;
				person.FirstName = user.FirstName;
				person.LastName = user.LastName;

				//var group = await _departmentGroupsService.GetGroupForUser(user.UserId);
				//if (group != null)
				//	person.Group = group.Name;
				person.Group = user.DepartmentGroupName;
				person.GroupId = user.DepartmentGroupId.GetValueOrDefault();

				//var roles = await _personnelRolesService.GetRolesForUser(user.UserId);
				person.Roles = new List<string>();
				foreach (var role in user.RoleNamesList)
				{
					person.Roles.Add(role);
				}

				personnelJson.Add(person);
			}

			switch (personnelSortOrder)
			{
				case PersonnelSortOrders.FirstName:
					personnelJson = personnelJson.OrderBy(x => x.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					personnelJson = personnelJson.OrderBy(x => x.LastName).ToList();
					break;
				case PersonnelSortOrders.Group:
					personnelJson = personnelJson.OrderBy(x => x.GroupId).ToList();
					break;
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetPersonnelForCallGrid(string callLat, string callLong)
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);//.GetAllUsersForDepartmentUnlimitedMinusDisabled(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			var personnelStatusSortOrder = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			foreach (var user in users)
			{
				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				person.Name = await UserHelper.GetFullNameForUser(personnelNames, user.UserName, user.UserId);

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				if (group != null)
					person.Group = group.Name;

				var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
				person.Roles = new List<string>();
				foreach (var role in roles)
				{
					person.Roles.Add(role.Name);
				}

				var currentStaffing = userStates.FirstOrDefault(x => x.UserId == user.UserId);
				if (currentStaffing != null)
				{
					var staffing = await CustomStatesHelper.GetCustomPersonnelStaffing(DepartmentId, currentStaffing);

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
					var status = await CustomStatesHelper.GetCustomPersonnelStatus(DepartmentId, currentStatus);
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
					var eta = await _geoService.GetEtaInSecondsAsync(currentStatus.GeoLocationData, String.Format("{0},{1}", callLat, callLong));

					if (eta > 0)
						person.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
					else
						person.Eta = "N/A";
				}

				if (currentStatus != null)
				{
					if (personnelStatusSortOrder != null && personnelStatusSortOrder.Any())
					{
						var statusSorting = personnelStatusSortOrder.FirstOrDefault(x => x.StatusId == currentStatus.ActionTypeId);
						if (statusSorting != null)
							person.Weight = statusSorting.Weight;
						else
							person.Weight = 9000;
					}
					else
					{
						person.Weight = 9000;
					}
				}
				else
					person.Weight = 9000;

				personnelJson.Add(person);
			}

			switch (personnelSortOrder)
			{
				case PersonnelSortOrders.Default:
					personnelJson = personnelJson.OrderBy(x => x.Weight).ToList();
					break;
				case PersonnelSortOrders.FirstName:
					personnelJson = personnelJson.OrderBy(x => x.Weight).ThenBy(x => x.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					personnelJson = personnelJson.OrderBy(x => x.Weight).ThenBy(x => x.LastName).ToList();
					break;
				case PersonnelSortOrders.Group:
					personnelJson = personnelJson.OrderBy(x => x.Weight).ThenBy(x => x.GroupId).ToList();
					break;
				default:
					personnelJson = personnelJson.OrderBy(x => x.Weight).ToList();
					break;
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetPersonnelForGridWithFilter(bool filterSelf = false)
		{
			List<PersonnelForJson> personnelJson = new List<PersonnelForJson>();
			var dep = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(dep.DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var user in users)
			{
				if (filterSelf)
					if (user.UserId == UserId)
						continue;

				PersonnelForJson person = new PersonnelForJson();
				person.UserId = user.UserId;
				person.Name = await UserHelper.GetFullNameForUser(personnelNames, user.UserName, user.UserId);

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				if (group != null)
					person.Group = group.Name;

				var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
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
		public async Task<IActionResult> GetPersonnelList()
		{
			List<PersonnelForListJson> personnelJson = new List<PersonnelForListJson>();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedAsync(DepartmentId);
			var departmentMembers = await _departmentsService.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId);
			//var actionLogs = await _actionLogsService.GetActionLogsForDepartment(DepartmentId, true);
			//var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);
			var canGroupAdminsDelete = await _authorizationService.CanGroupAdminsRemoveUsersAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId);
			var userGroupRoles = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, true, true, false);

			var sortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);

			foreach (var user in users)
			{
				var person = new PersonnelForListJson();
				person.UserId = user.UserId.ToString();

				var member = departmentMembers.FirstOrDefault(x => x.UserId == user.UserId);
				//var actionLog = actionLogs.FirstOrDefault(x => x.UserId == user.UserId);
				//var userProfile = await _userProfileService.GetProfileByUserId(user.UserId);

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

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				if (group != null)
				{
					person.Group = group.Name;

					if (department.IsUserAnAdmin(UserId) || (canGroupAdminsDelete && group.IsUserGroupAdmin(UserId) && !department.IsUserAnAdmin(user.UserId)))
						person.CanRemoveUser = true;

					if (group.IsUserGroupAdmin(UserId) || department.IsUserAnAdmin(UserId))
						person.CanEditUser = true;

					person.GroupId = group.DepartmentGroupId;
				}
				else
				{
					if (department.IsUserAnAdmin(UserId))
					{
						person.CanRemoveUser = true;
						person.CanEditUser = true;
					}
				}

				//var roles = await _personnelRolesService.GetRolesForUser(user.UserId);
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
				case PersonnelSortOrders.Group:
					return Json(personnelJson.OrderBy(x => x.GroupId));
				default:
					return Json(personnelJson);
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetPersonnelListPaged(int perPage, int page)
		{
			PersonnelListPagedResult result = new PersonnelListPagedResult();
			result.Data = new List<PersonnelForListJson>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedAsync(DepartmentId);
			var departmentMembers = await _departmentsService.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId);
			//var actionLogs = await _actionLogsService.GetActionLogsForDepartment(DepartmentId, true);
			//var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);
			var canGroupAdminsDelete = await _authorizationService.CanGroupAdminsRemoveUsersAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId);
			var userGroupRoles = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, false);

			var sortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);

			foreach (var user in users)
			{
				var person = new PersonnelForListJson();
				person.UserId = user.UserId.ToString();

				var member = departmentMembers.FirstOrDefault(x => x.UserId == user.UserId);
				//var actionLog = actionLogs.FirstOrDefault(x => x.UserId == user.UserId);
				//var userProfile = await _userProfileService.GetProfileByUserId(user.UserId);

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

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
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

				//var roles = await _personnelRolesService.GetRolesForUser(user.UserId);
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
		public async Task<IActionResult> GetPersonnelStatusHtmlForDropdownByStateId(int customStateId)
		{
			string buttonHtml = string.Empty;

			CustomState customStates = null;
			List<CustomStateDetail> activeDetails = null;

			if (customStateId > 25)
			{
				customStates = await _customStateService.GetCustomSateByIdAsync(customStateId);

				if (customStates != null)
				{
					activeDetails = customStates.GetActiveDetails();
				}
			}

			if (activeDetails == null)
				activeDetails = _customStateService.GetDefaultPersonStatuses();

			StringBuilder sb = new StringBuilder();

			foreach (var state in activeDetails.OrderBy(x => x.Order))
			{
				sb.Append($"<option value='{state.CustomStateDetailId}'>{state.ButtonText}</option>");
			}

			buttonHtml = sb.ToString();


			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> GetPersonnelStaffingHtmlForDropdownByStateId(int customStateId)
		{
			string buttonHtml = string.Empty;

			CustomState customStates = null;
			List<CustomStateDetail> activeDetails = null;

			if (customStateId > 25)
			{
				customStates = await _customStateService.GetCustomSateByIdAsync(customStateId);

				if (customStates != null)
				{
					activeDetails = customStates.GetActiveDetails();
				}
			}

			if (activeDetails == null)
				activeDetails = _customStateService.GetDefaultPersonStaffings();

			StringBuilder sb = new StringBuilder();

			foreach (var state in activeDetails.OrderBy(x => x.Order))
			{
				sb.Append($"<option value='{state.CustomStateDetailId}'>{state.ButtonText}</option>");
			}

			buttonHtml = sb.ToString();


			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> GetPersonnelStatusDestinationHtmlForDropdown(int customStateId, int customStatusDetailId)
		{
			string buttonHtml = string.Empty;

			CustomState customStates = null;
			List<CustomStateDetail> activeDetails = null;

			if (customStateId > 25)
			{
				customStates = await _customStateService.GetCustomSateByIdAsync(customStateId);

				if (customStates != null)
				{
					activeDetails = customStates.GetActiveDetails();
				}
			}

			if (activeDetails == null)
				activeDetails = _customStateService.GetDefaultPersonStatuses();

			var state = activeDetails.FirstOrDefault(x => x.CustomStateDetailId == customStatusDetailId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			StringBuilder sb = new StringBuilder();

			sb.Append($"<option value='-1'>None</option>");

			if (state != null)
			{
				// No custom drop down options for responding to avoid confusion between Responding Station and Responding Scene
				if (customStatusDetailId != (int)ActionTypes.Responding)
				{
					if (state.DetailType == (int)CustomStateDetailTypes.None)
					{

					}
					else if (state.DetailType == (int)CustomStateDetailTypes.Calls)
					{
						foreach (var call in activeCalls)
						{
							sb.Append($"<option value='{call.CallId}'>Call {call.GetIdentifier()}:{call.Name}</option>");
						}
					}
					else if (state.DetailType == (int)CustomStateDetailTypes.Stations)
					{
						foreach (var station in stations)
						{
							sb.Append($"<option value='{station.DepartmentGroupId}'>Station: {station.Name}</option>");
						}

						sb.Append("</ul>");
					}
					else if (state.DetailType == (int)CustomStateDetailTypes.CallsAndStations)
					{
						foreach (var call in activeCalls)
						{
							sb.Append($"<option value='{call.CallId}'>Call {call.GetIdentifier()}:{call.Name}</option>");
						}

						foreach (var station in stations)
						{
							sb.Append($"<option value='{station.DepartmentGroupId}'>Station: {station.Name}</option>");
						}
					}
				}
			}

			buttonHtml = sb.ToString();


			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> SetActionForUser(string userId, int actionType, int destination, string note, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewPersonAsync(UserId, userId, DepartmentId))
				Unauthorized();

			var status = new ActionLog();
			status.UserId = userId;
			status.Timestamp = DateTime.UtcNow;
			status.ActionTypeId = actionType;
			status.DepartmentId = DepartmentId;

			if (destination > 0)
				status.DestinationId = destination;

			if (!String.IsNullOrWhiteSpace(note))
				status.Note = HttpUtility.UrlDecode(note);

			try
			{
				var savedState = await _actionLogsService.SaveActionLogAsync(status, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> SetUserActionForMultiple(string userIds, int actionType, int destination, string note, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(userIds) && userIds.Split(char.Parse("|")).Any())
			{
				foreach (var userId in userIds.Split(char.Parse("|")))
				{
					if (!await _authorizationService.CanUserViewPersonAsync(UserId, userId, DepartmentId))
						Unauthorized();

					var status = new ActionLog();
					status.UserId = userId;
					status.Timestamp = DateTime.UtcNow;
					status.ActionTypeId = actionType;
					status.DepartmentId = DepartmentId;

					if (destination > 0)
						status.DestinationId = destination;

					if (!String.IsNullOrWhiteSpace(note))
						status.Note = HttpUtility.UrlDecode(note);

					try
					{
						var savedState = await _actionLogsService.SaveActionLogAsync(status, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> SetStaffingForUser(string userId, int staffing, string note, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewPersonAsync(UserId, userId, DepartmentId))
				Unauthorized();

			string staffingNote = String.Empty;
			if (!String.IsNullOrWhiteSpace(note))
				staffingNote = HttpUtility.UrlDecode(note);

			try
			{
				var savedState = await _userStateService.CreateUserStateAsync(userId, DepartmentId, staffing, staffingNote, DateTime.UtcNow, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> SetUserStaffingForMultiple(string userIds, int staffing, string note, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(userIds) && userIds.Split(char.Parse("|")).Any())
			{
				string staffingNote = String.Empty;
				if (!String.IsNullOrWhiteSpace(note))
					staffingNote = HttpUtility.UrlDecode(note);

				foreach (var userId in userIds.Split(char.Parse("|")))
				{
					if (!await _authorizationService.CanUserViewPersonAsync(UserId, userId, DepartmentId))
						Unauthorized();

					try
					{
						var savedState = await _userStateService.CreateUserStateAsync(userId, DepartmentId, staffing, staffingNote, DateTime.UtcNow, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetPersonnelGroupStatusForGridCombined()
		{
			List<GroupStatusJson> personnelGroupJson = new List<GroupStatusJson>();
			var dep = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(dep.DepartmentId);
			var lastUserActionlogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(dep.DepartmentId);
			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(dep.DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			List<UserState> userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				if (!await _departmentsService.IsUserDisabledAsync(u.UserId, DepartmentId) && !await _departmentsService.IsUserHiddenAsync(u.UserId, DepartmentId))
				{
					userStates.Add(await _userStateService.GetLastUserStateByUserIdAsync(u.UserId));
				}
			}

			foreach (var departmentGroup in groups)
			{
				GroupStatusJson group = new GroupStatusJson();
				group.GroupId = departmentGroup.DepartmentGroupId;
				group.Name = departmentGroup.Name;
				group.Personnel = new List<PersonnelStatusJson>();

				var sortedUsers = from u in departmentGroup.Members
													  join pn in personnelNames on u.UserId equals pn.UserId
													  let name = pn.Name
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
		public async Task<IActionResult> GetPersonnelGroupStatusForGrid()
		{
			List<GroupStatusJson> personnelGroupJson = new List<GroupStatusJson>();
			var dep = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(dep.DepartmentId);

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
		public async Task<IActionResult> GetPersonnelGroupMemberStatusForGrid(int groupId)
		{
			List<PersonnelStatusJson> personnel = new List<PersonnelStatusJson>();
			var dep = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			var lastUserActionlogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(dep.DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			if (groupId == 0)
			{
				var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(dep.DepartmentId);
				var groupedUserIds = await _departmentGroupsService.AllGroupedUserIdsForDepartmentAsync(dep.DepartmentId);

				var unGroupedUsers = from u in allUsers
														 where !(from uid in groupedUserIds
																		 select uid).Contains(u.UserId)
														 select u;

				var sortedUsers = from u in unGroupedUsers
													join pn in personnelNames on u.UserId equals pn.UserId
													let name = pn.Name
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

					var level = await _userStateService.GetLastUserStateByUserIdAsync(member.User.UserId);

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
				var depGroup = await _departmentGroupsService.GetGroupByIdAsync(groupId);

				var sortedUsers = from u in depGroup.Members
													join pn in personnelNames on u.UserId equals pn.UserId
													let name = pn.Name
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

					var level = await _userStateService.GetLastUserStateByUserIdAsync(member.User.UserId);

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
		public async Task<IActionResult> GetMembersForRole(int id)
		{
			var role = await _personnelRolesService.GetRoleByIdAsync(id);

			return Json(role.Users.Select(x => x.UserId).ToList());
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> ReactivateUser(string id, CancellationToken cancellationToken)
		{
			ViewPersonView model = new ViewPersonView();
			model.Profile = await _userProfileService.GetProfileByUserIdAsync(id, true);
			model.User = _usersService.GetUserById(id);

			var member = await _departmentsService.GetDepartmentMemberAsync(id, DepartmentId);
			if (member != null)
				model.Department = await _departmentsService.GetDepartmentByIdAsync(member.DepartmentId);
			else
				model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Group = await _departmentGroupsService.GetGroupForUserAsync(id, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(id, DepartmentId);

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

			await _departmentsService.ReactivateUserAsync(DepartmentId, id, cancellationToken);

			_userProfileService.ClearAllUserProfilesFromCache(DepartmentId);
			_departmentsService.InvalidateDepartmentUsersInCache(DepartmentId);
			_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
			_departmentsService.InvalidateDepartmentMembers();
			_usersService.ClearCacheForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult> AddExistingUser(string id, CancellationToken cancellationToken)
		{
			ViewPersonView model = new ViewPersonView();
			model.Profile = await _userProfileService.GetProfileByUserIdAsync(id, true);
			model.User = _usersService.GetUserById(id, true);

			var member = await _departmentsService.GetDepartmentMemberAsync(id, DepartmentId);
			if (member != null)
				model.Department = await _departmentsService.GetDepartmentByIdAsync(member.DepartmentId);
			else
				model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Group = await _departmentGroupsService.GetGroupForUserAsync(id, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(id, DepartmentId);

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

			await _departmentsService.AddExistingUserAsync(DepartmentId, id, cancellationToken);

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
		public async Task<IActionResult> GetRolesForCallGrid()
		{
			List<RoleForJson> rolesJson = new List<RoleForJson>();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> Roles()
		{
			PersonnelRolesModel model = new PersonnelRolesModel();
			model.Roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			model.CanAddNewRole = true;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_Create)]
		public async Task<IActionResult> AddRole()
		{
			AddRoleModel model = new AddRoleModel();
			model.Role = new PersonnelRole();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Role_Create)]
		public async Task<IActionResult> AddRole(AddRoleModel model, CancellationToken cancellationToken)
		{
			model.Role.DepartmentId = DepartmentId;

			if (await _personnelRolesService.GetRoleByDepartmentAndNameAsync(model.Role.DepartmentId, model.Role.Name) != null)
				ModelState.AddModelError("Role.Name", "Role with that name already exists in the department.");

			if (ModelState.IsValid)
			{
				await _personnelRolesService.SaveRoleAsync(model.Role, cancellationToken);

				return RedirectToAction("Roles");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_Update)]
		public async Task<IActionResult> EditRole(int roleId)
		{
			if (!await _authorizationService.CanUserEditRoleAsync(UserId, roleId))
				Unauthorized();

			EditRoleModel model = new EditRoleModel();
			model.Role = await _personnelRolesService.GetRoleByIdAsync(roleId);
			model.Users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Role_Update)]
		public async Task<IActionResult> EditRole(EditRoleModel model, IFormCollection collection, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditRoleAsync(UserId, model.Role.PersonnelRoleId))
				Unauthorized();

			var role = await _personnelRolesService.GetRoleByIdAsync(model.Role.PersonnelRoleId);
			role.Name = model.Role.Name;
			role.Description = model.Role.Description;

			var existingRole = await _personnelRolesService.GetRoleByDepartmentAndNameAsync(model.Role.DepartmentId, model.Role.Name);
			if (existingRole != null && existingRole.PersonnelRoleId != model.Role.PersonnelRoleId)
				ModelState.AddModelError("Role.Name", "Role with that name already exists in the department.");

			if (ModelState.IsValid)
			{
				//using (var scope = new TransactionScope())
				//{
				await _personnelRolesService.DeleteRoleUsersAsync(role.Users.ToList(), cancellationToken);

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

				await _personnelRolesService.SaveRoleAsync(role, cancellationToken);

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
		public async Task<IActionResult> DeleteRole(int roleId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditRoleAsync(UserId, roleId))
				Unauthorized();

			await _personnelRolesService.DeleteRoleByIdAsync(roleId, cancellationToken);

			return RedirectToAction("Roles");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Role_View)]
		public async Task<IActionResult> ViewRole(int roleId)
		{
			if (!await _authorizationService.CanUserViewRoleAsync(UserId, roleId))
				Unauthorized();

			ViewRoleModel model = new ViewRoleModel();
			model.Role = await _personnelRolesService.GetRoleByIdAsync(roleId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]

		public async Task<IActionResult> GetRoles()
		{
			var rolesJson = new List<RoleForJson>();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

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

		public async Task<IActionResult> GetCertifications()
		{
			var certificationsJson = new List<CertificationJson>();
			var certifications = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);

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

		public async Task<IActionResult> GetRolesForUser(string userId)
		{
			var rolesJson = new List<RoleForJson>();
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, DepartmentId);

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
