using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Helpers;
using RestSharp;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Providers.Bus;
using Resgrid.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Helpers;
using Resgrid.Web.Areas.User.Models.BigBoardX;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]

	[ClaimsResource(ResgridClaimTypes.Resources.Department)]
	public class HomeController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPushUriService _pushUriService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallsService _callsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitsService _unitsService;
		private readonly IAddressService _addressService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IPushService _pushService;
		private readonly ILimitsService _limitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IOptions<AppOptions> _appOptionsAccessor;
		private readonly UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> _userManager;

		public HomeController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IUserStateService userStateService, IDepartmentGroupsService departmentGroupsService, IPushUriService pushUriService, Resgrid.Model.Services.IAuthorizationService authorizationService,
			IUserProfileService userProfileService, ICallsService callsService, IGeoLocationProvider geoLocationProvider, IDepartmentSettingsService departmentSettingsService,
			IUnitsService unitsService, IAddressService addressService, IPersonnelRolesService personnelRolesService, IPushService pushService, ILimitsService limitsService,
			ICustomStateService customStateService, IEventAggregator eventAggregator, IOptions<AppOptions> appOptionsAccessor, UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> userManager)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_pushUriService = pushUriService;
			_authorizationService = authorizationService;
			_userProfileService = userProfileService;
			_callsService = callsService;
			_geoLocationProvider = geoLocationProvider;
			_departmentSettingsService = departmentSettingsService;
			_unitsService = unitsService;
			_addressService = addressService;
			_personnelRolesService = personnelRolesService;
			_pushService = pushService;
			_limitsService = limitsService;
			_customStateService = customStateService;
			_eventAggregator = eventAggregator;
			_appOptionsAccessor = appOptionsAccessor;
			_userManager = userManager;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult Dashboard(bool firstRun = false)
		{
			var model = new DashboardModel();

			var staffingLevel = _userStateService.GetLastUserStateByUserId(UserId);
			model.UserState = staffingLevel.State;
			model.StateNote = staffingLevel.Note;

			var staffingLevels = _customStateService.GetActiveStaffingLevelsForDepartment(DepartmentId);
			if (staffingLevels == null)
			{
				model.UserStateTypes = model.UserStateEnum.ToSelectList();
				ViewBag.UserStateTypes = model.UserStateEnum.ToSelectList();
			}
			else
			{
				model.CustomStaffingActive = true;
				var selected = staffingLevels.Details.FirstOrDefault(x => x.CustomStateDetailId == staffingLevel.State);
				model.UserStateTypes = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText", selected);
				ViewBag.UserStateTypes = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText", selected);
			}

			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);
			model.FirstRun = firstRun;
			model.Number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			model.States = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);

			return View(model);
		}

		#region Partials
		public IActionResult ActivityStats()
		{
			var model = new ActivityStatsModel();
			var actions = _actionLogsService.GetActionsCountForLast7DaysForDepartment(DepartmentId);
			var report = _departmentsService.GetDepartmentSetupReport(DepartmentId);

			if (report != null)
				model.SetupScore = (int)_departmentsService.GenerateSetupScore(report);

			float actionChange = 0;

			if (actions[1] != 0 && actions[0] != 0)
				actionChange = actions[1] - actions[0] / actions[1];
			else if (actions[0] > 0)
				actionChange = 100;

			if (actionChange > 0)
				model.ActivityChange = string.Format("+{0}%", actionChange.ToString());
			else if (actionChange < 0)
				model.ActivityChange = string.Format("-{0}%", actionChange.ToString());
			else
				model.ActivityChange = string.Format("{0}%", actionChange.ToString());

			model.ActivityCount = actions.Sum().ToString();
			model.ActivityNumbers = string.Format("{0},{1},{2},{3},{4},{5},{6}", actions[6], actions[5], actions[4], actions[3],
				actions[2], actions[1], actions[0]);

			//var logs = _workLogsService.GetLogsCountForLast7DaysForDepartment(DepartmentId);
			//float logsChange = 0;

			//if (logs[1] != 0 && logs[0] != 0)
			//	logsChange = logs[1] - logs[0] / logs[1];
			//else if (logs[0] > 0)
			//	logsChange = 100;

			//if (logsChange > 0)
			//	model.LogsChange = string.Format("+{0}%", logsChange.ToString());
			//else if (logsChange < 0)
			//	model.LogsChange = string.Format("-{0}%", logsChange.ToString());
			//else
			//	model.LogsChange = string.Format("{0}%", logsChange.ToString());

			//model.LogsCount = logs.Sum().ToString();
			//model.LogsNumbers = string.Format("{0},{1},{2},{3},{4},{5},{6}", logs[6], logs[5], logs[4], logs[3],
			//	logs[2], logs[1], logs[0]);

			var calls = _callsService.GetCallsCountForLast7DaysForDepartment(DepartmentId);
			float callsChange = 0;

			if (calls[1] != 0 && calls[0] != 0)
				callsChange = calls[1] - calls[0] / calls[1];
			else if (calls[0] > 0)
				callsChange = 100;

			if (callsChange > 0)
				model.CallsChange = string.Format("+{0}%", callsChange.ToString());
			else if (callsChange < 0)
				model.CallsChange = string.Format("-{0}%", callsChange.ToString());
			else
				model.CallsChange = string.Format("{0}%", callsChange.ToString());

			model.CallsCount = calls.Sum().ToString();
			model.CallsNumbers = string.Format("{0},{1},{2},{3},{4},{5},{6}", calls[6], calls[5], calls[4], calls[3],
				calls[2], calls[1], calls[0]);

			return View("_ActivityStatsPartial", model);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetUserStatusTable()
		{
			var model = new UserStatusTableModel();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);
			model.LastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			model.UserStates = new List<UserState>();
			model.DepartmentGroups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			model.Stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);
			model.UsersGroup = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
			model.States = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);
			model.StaffingLevels = _customStateService.GetActiveStaffingLevelsForDepartment(DepartmentId);

			var userStates = _userStateService.GetLatestStatesForDepartment(DepartmentId);
			//var allRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);
			//var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

			//var allUsers = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			var allUsers = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, false, false, false);
			model.ExcludedUsers = _departmentsService.GetAllDisabledOrHiddenUsers(DepartmentId);

			List<string> groupedUserIds = new List<string>();

			foreach (var dg in model.DepartmentGroups)
			{
				var membersToProcess = from member in dg.Members
									   where !(model.ExcludedUsers.Any(item2 => item2 == member.UserId))
									   select member;

				foreach (var u in membersToProcess)
				{
					if (allUsers.Any(x => x.UserId == u.UserId))
					{
						groupedUserIds.Add(u.UserId);

						UserState state = userStates.FirstOrDefault(x => x.UserId == u.UserId);

						if (state == null)
						{
							state = new UserState();
							state.UserId = u.UserId;
							state.AutoGenerated = true;
							state.Timestamp = DateTime.UtcNow;
							state.State = (int)UserStateTypes.Available;
						}

						model.DepartmentUserStates.Add(u.UserId, state);
					}
				}



				var allGroupMembers = new List<DepartmentGroupMember>(dg.Members);

				foreach (var allMember in allGroupMembers)
				{
					if (!allUsers.Any(x => x.UserId == allMember.UserId))
					{
						dg.Members.Remove(allMember);
					}
				}
			}

			var ungroupedUsers = from u in allUsers
								 where !(groupedUserIds.Contains(u.UserId)) && !(model.ExcludedUsers.Any(item2 => item2 == u.UserId))
								 select u;

			foreach (var u in ungroupedUsers)
			{
				model.UnGroupedUsers.Add(u.UserId);
			}

			foreach (var u in allUsers)
			{
				UserState state = userStates.FirstOrDefault(x => x.UserId == u.UserId);

				if (state == null)
				{
					state = new UserState();
					state.UserId = u.UserId;
					state.AutoGenerated = true;
					state.Timestamp = DateTime.UtcNow;
					state.State = (int)UserStateTypes.Available;
				}

				model.UserStates.Add(state);

				//var name = personnelNames.FirstOrDefault(x => x.UserId == u.UserId);
				var name = u.Name;

				if (name == null)
					name = UserHelper.GetFullNameForUser(u.UserId);

				if (!model.Names.ContainsKey(u.UserId))
					model.Names.Add(u.UserId, name);

				//var rolesText = new StringBuilder();

				//if (allRoles.ContainsKey(u.UserId))
				//{
				//	var roles = allRoles[u.UserId];

				//	foreach (var role in roles)
				//	{
				//		if (rolesText.Length > 0)
				//			rolesText.Append(", " + role.Name);
				//		else
				//			rolesText.Append(role.Name);
				//	}
				//}

				if (!model.Roles.ContainsKey(u.UserId))
					model.Roles.Add(u.UserId, u.RoleNames);
			}

			return PartialView("_UserStatusTablePartial", model);
		}

		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult UserActionsPartial()
		{
			var model = new UserActionsPartialView();
			model.States = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);

			return View("_UserActionsPartial", model);
		}

		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult PersonnelActionButtonsPartial()
		{
			var model = new PersonnelActionButtonsPartialView();
			model.States = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);
			model.StaffingLevels = _customStateService.GetActiveStaffingLevelsForDepartment(DepartmentId);

			return View("_UserActionsPartial", model);
		}
		#endregion Partials

		#region Edit User Profile
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult EditUserProfile(string userId)
		{
			var model = new EditProfileModel();
			model.ApiUrl = _appOptionsAccessor.Value.ResgridApiUrl;
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			var departmentMember = _departmentsService.GetDepartmentMember(userId, DepartmentId);

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(_departmentGroupsService.GetAllGroupsForDepartment(model.Department.DepartmentId));

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");

			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			var group = _departmentGroupsService.GetGroupForUser(userId, DepartmentId);

			if (group != null)
			{
				model.UserGroup = group.DepartmentGroupId;
				model.IsUserGroupAdmin = group.IsUserGroupAdmin(userId);
			}

			//model.UsersRoles = _personnelRolesService.GetRolesForUser(userId);
			model.IsDisabled = departmentMember.IsDisabled.HasValue != false && departmentMember.IsDisabled.Value;
			model.IsHidden = departmentMember.IsHidden.HasValue != false && departmentMember.IsHidden.Value;
			model.IsDepartmentAdmin = departmentMember.IsAdmin.HasValue != false && departmentMember.IsAdmin.Value;
			model.CanEnableVoice = _limitsService.CanDepartmentUseVoice(DepartmentId);

			if (userId == UserId)
				model.IsOwnProfile = true;

			model.User = _usersService.GetUserById(userId, true);
			model.UserId = userId;
			model.Email = model.User.Email;

			//model.Profile = _userProfileService.GetProfileByUserId(userId, true);
			model.Profile = _userProfileService.GetUserProfileForEditing(userId);

			if (model.Profile == null)
				model.Profile = new UserProfile();

			if (model.Profile.Image == null)
				model.HasCustomIamge = false;
			else
				model.HasCustomIamge = true;

			if (model.Profile != null && model.Profile.HomeAddressId.HasValue)
			{
				var homeAddress = _addressService.GetAddressById(model.Profile.HomeAddressId.Value);
				model.PhysicalAddress1 = homeAddress.Address1;
				model.PhysicalCity = homeAddress.City;
				model.PhysicalCountry = homeAddress.Country;
				model.PhysicalPostalCode = homeAddress.PostalCode;
				model.PhysicalState = homeAddress.State;
			}

			if (model.Profile != null && model.Profile.MailingAddressId.HasValue && model.Profile.HomeAddressId.HasValue &&
				(model.Profile.MailingAddressId.Value == model.Profile.HomeAddressId.Value))
			{
				model.MailingAddressSameAsPhysical = true;
			}
			else if (model.Profile != null && model.Profile.MailingAddressId.HasValue)
			{
				var mailingAddress = _addressService.GetAddressById(model.Profile.MailingAddressId.Value);
				model.MailingAddress1 = mailingAddress.Address1;
				model.MailingCity = mailingAddress.City;
				model.MailingCountry = mailingAddress.Country;
				model.MailingPostalCode = mailingAddress.PostalCode;
				model.MailingState = mailingAddress.State;
			}

			if (model.Profile != null)
				model.Carrier = (MobileCarriers)model.Profile.MobileCarrier;

			if (!String.IsNullOrEmpty(model.Profile.FirstName) && !String.IsNullOrEmpty(model.Profile.LastName))
			{
				model.FirstName = model.Profile.FirstName;
				model.LastName = model.Profile.LastName;
			}
			else
			{
				//MembershipUser currentUser = Membership.GetUser(model.User.UserName, userIsOnline: true);
				//var pfile = ProfileBase.Create(model.User.UserName, true);

				var userProfile = _userProfileService.GetProfileByUserId(userId);

				if (userProfile != null)
				{
					model.FirstName = userProfile.FirstName;
					model.LastName = userProfile.LastName;
				}
				else
				{
					model.FirstName = "";
					model.LastName = "";
				}
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> EditUserProfile(EditProfileModel model, IFormCollection form)
		{
			model.User = _usersService.GetUserById(model.UserId);
			//model.PushUris = _pushUriService.GetPushUrisByUserId(model.UserId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.CanEnableVoice = _limitsService.CanDepartmentUseVoice(DepartmentId);

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(_departmentGroupsService.GetAllGroupsForDepartment(model.Department.DepartmentId));
			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");

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

			if ((model.Profile.SendSms || model.Profile.SendMessageSms || model.Profile.SendMessageSms) && String.IsNullOrEmpty(model.Profile.MobileNumber))
			{
				ModelState.AddModelError("Profile.MobileNumber", "You have selected you want SMS/Text notifications but have not supplied a mobile number.");
			}

			// They specified a street address for physical
			if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
			{
				if (String.IsNullOrEmpty(model.PhysicalCity))
					ModelState.AddModelError("City", string.Format("The Physical City field is required"));

				if (String.IsNullOrEmpty(model.PhysicalCountry))
					ModelState.AddModelError("Country", string.Format("The Physical Country field is required"));

				if (String.IsNullOrEmpty(model.PhysicalPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Physical Postal Code field is required"));

				if (String.IsNullOrEmpty(model.PhysicalState))
					ModelState.AddModelError("State", string.Format("The Physical State/Provence field is required"));
			}

			if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
			{
				if (String.IsNullOrEmpty(model.MailingCity))
					ModelState.AddModelError("City", string.Format("The Mailing City field is required"));

				if (String.IsNullOrEmpty(model.MailingCountry))
					ModelState.AddModelError("Country", string.Format("The Mailing Country field is required"));

				if (String.IsNullOrEmpty(model.MailingPostalCode))
					ModelState.AddModelError("PostalCode", string.Format("The Mailing Postal Code field is required"));

				if (String.IsNullOrEmpty(model.MailingState))
					ModelState.AddModelError("State", string.Format("The Mailing State/Provence field is required"));
			}

			if (model.User.Email != model.Email)
			{
				var currentEmail = _usersService.GetUserByEmail(model.Email);

				if (currentEmail != null && currentEmail.Id != model.User.UserId.ToString())
					ModelState.AddModelError("Email", "Email Address Already in Use. Please use another one.");
			}

			if (model.Profile.VoiceForCall)
			{
				if (model.Profile.VoiceCallHome && String.IsNullOrWhiteSpace(model.Profile.HomeNumber))
					ModelState.AddModelError("VoiceForCall", "You selected to Enable Telephone alerting for your home phone number but have not supplied a home phone number. Please supply one.");

				if (model.Profile.VoiceCallMobile && String.IsNullOrWhiteSpace(model.Profile.MobileNumber))
					ModelState.AddModelError("VoiceForCall", "You selected to Enable Telephone alerting for your mobile phone number but have not supplied a mobile phone number. Please supply one.");

				if (!model.Profile.VoiceCallHome && !model.Profile.VoiceCallMobile)
					ModelState.AddModelError("VoiceForCall", "You selected to Enable Telephone alerting, but you didn't select a number to call you at. Please select either/both home phone or mobile phone.");
			}

			if (model.IsOwnProfile)
			{
				bool checkPasswordSuccess = false;
				if (string.IsNullOrEmpty(model.OldPassword) == false && string.IsNullOrEmpty(model.NewPassword) == false)
				{
					try
					{
						checkPasswordSuccess = await _userManager.CheckPasswordAsync(model.User, model.OldPassword);
					}
					catch (Exception)
					{
						checkPasswordSuccess = false;
					}

					if (!checkPasswordSuccess)
					{
						ModelState.AddModelError("", "The current password is incorrect or the new password is invalid.");
					}
				}

				if (!String.IsNullOrWhiteSpace(model.NewUsername))
				{
					var newUser = await _userManager.FindByNameAsync(model.NewUsername);

					if (newUser != null)
						ModelState.AddModelError("", "The NEW username you have supplied is already in use, please try another one. If you didn't mean to update your username please leave that field blank.");
				}
			}

			if (ModelState.IsValid)
			{
				Address homeAddress = null;
				Address mailingAddress = null;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.ProfileUpdated;

				var savedProfile = _userProfileService.GetUserProfileForEditing(model.UserId);

				if (savedProfile == null)
					savedProfile = new UserProfile();

				auditEvent.Before = savedProfile.CloneJson();

				savedProfile.UserId = model.UserId;
				savedProfile.MobileCarrier = (int)model.Carrier;
				savedProfile.FirstName = model.FirstName;
				savedProfile.LastName = model.LastName;
				savedProfile.MobileNumber = model.Profile.MobileNumber;
				savedProfile.SendEmail = model.Profile.SendEmail;
				savedProfile.SendPush = model.Profile.SendPush;
				savedProfile.SendSms = model.Profile.SendSms;
				savedProfile.SendMessageEmail = model.Profile.SendMessageEmail;
				savedProfile.SendMessagePush = model.Profile.SendMessagePush;
				savedProfile.SendMessageSms = model.Profile.SendMessageSms;
				savedProfile.SendNotificationEmail = model.Profile.SendNotificationEmail;
				savedProfile.SendNotificationPush = model.Profile.SendNotificationPush;
				savedProfile.SendNotificationSms = model.Profile.SendNotificationSms;
				savedProfile.DoNotRecieveNewsletters = model.Profile.DoNotRecieveNewsletters;
				savedProfile.HomeNumber = model.Profile.HomeNumber;
				savedProfile.IdentificationNumber = model.Profile.IdentificationNumber;
				savedProfile.TimeZone = model.Profile.TimeZone;

				if (model.CanEnableVoice)
				{
					savedProfile.VoiceForCall = model.Profile.VoiceForCall;
					savedProfile.VoiceCallHome = model.Profile.VoiceCallHome;
					savedProfile.VoiceCallMobile = model.Profile.VoiceCallMobile;
				}
				else
				{
					savedProfile.VoiceForCall = false;
					savedProfile.VoiceCallHome = false;
					savedProfile.VoiceCallMobile = false;
				}

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				{
					var currentGroup = _departmentGroupsService.GetGroupForUser(model.UserId, DepartmentId);
					if (model.UserGroup != 0 && (currentGroup == null || currentGroup.DepartmentGroupId != model.UserGroup))
						_departmentGroupsService.MoveUserIntoGroup(model.UserId, model.UserGroup, model.IsUserGroupAdmin, DepartmentId);
					else if (currentGroup != null && currentGroup.DepartmentGroupId == model.UserGroup)
					{
						var member = _departmentGroupsService.GetGroupMemberForUser(model.UserId, DepartmentId);

						if (member != null)
						{
							member.IsAdmin = model.IsUserGroupAdmin;
							_departmentGroupsService.SaveGroupMember(member);
						}
					}
					else if (model.UserGroup <= 0)
						_departmentGroupsService.DeleteUserFromGroups(model.UserId, DepartmentId);
				}

				if (form.ContainsKey("roles"))
				{
					var roles = form["roles"].ToString().Split(char.Parse(","));

					if (roles.Any())
						_personnelRolesService.SetRolesForUser(DepartmentId, model.UserId, roles);
				}

				if (savedProfile.HomeAddressId.HasValue)
					homeAddress = _addressService.GetAddressById(savedProfile.HomeAddressId.Value);

				if (savedProfile.MailingAddressId.HasValue)
					mailingAddress = _addressService.GetAddressById(savedProfile.MailingAddressId.Value);

				if (!model.MailingAddressSameAsPhysical && homeAddress != null && mailingAddress != null &&
					(homeAddress.AddressId == mailingAddress.AddressId))
					mailingAddress = new Address();

				if (!String.IsNullOrWhiteSpace(model.PhysicalAddress1))
				{
					if (homeAddress == null)
						homeAddress = new Address();

					homeAddress.Address1 = model.PhysicalAddress1;
					homeAddress.City = model.PhysicalCity;
					homeAddress.Country = model.PhysicalCountry;
					homeAddress.PostalCode = model.PhysicalPostalCode;
					homeAddress.State = model.PhysicalState;

					homeAddress = _addressService.SaveAddress(homeAddress);
					savedProfile.HomeAddressId = homeAddress.AddressId;

					if (model.MailingAddressSameAsPhysical)
						savedProfile.MailingAddressId = homeAddress.AddressId;
				}

				if (!String.IsNullOrWhiteSpace(model.MailingAddress1) && !model.MailingAddressSameAsPhysical)
				{
					if (mailingAddress == null)
						mailingAddress = new Address();

					mailingAddress.Address1 = model.MailingAddress1;
					mailingAddress.City = model.MailingCity;
					mailingAddress.Country = model.MailingCountry;
					mailingAddress.PostalCode = model.MailingPostalCode;
					mailingAddress.State = model.MailingState;

					mailingAddress = _addressService.SaveAddress(mailingAddress);
					savedProfile.MailingAddressId = mailingAddress.AddressId;
				}

				savedProfile.LastUpdated = DateTime.UtcNow;
				_userProfileService.SaveProfile(DepartmentId, savedProfile);

				auditEvent.After = savedProfile.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				var depMember = _departmentsService.GetDepartmentMember(model.UserId, DepartmentId);
				if (depMember != null)
				{
					// Users Department Admin status changes, invalid the department object in cache.
					if (model.IsDepartmentAdmin != depMember.IsAdmin)
						_departmentsService.InvalidateDepartmentInCache(depMember.DepartmentId);

					depMember.IsAdmin = model.IsDepartmentAdmin;
					depMember.IsDisabled = model.IsDisabled;
					depMember.IsHidden = model.IsHidden;

					_departmentsService.SaveDepartmentMember(depMember);
				}

				if (!model.Profile.DoNotRecieveNewsletters)
					Unsubscribe(model.Email);

				//var membershipUser = Membership.GetUser(model.UserId);
				//membershipUser.Email = model.Email;
				//Membership.UpdateUser(membershipUser);

				_usersService.UpdateEmail(model.User.Id, model.Email);

				if (model.IsOwnProfile)
				{
					// Change Password
					if (!string.IsNullOrEmpty(model.OldPassword) && !string.IsNullOrEmpty(model.NewPassword))
					{
						var identityUser = await _userManager.FindByIdAsync(model.User.Id);
						var result = await _userManager.ChangePasswordAsync(identityUser, model.OldPassword, model.NewPassword);
					}

					if (!string.IsNullOrWhiteSpace(model.NewUsername))
					{
						_usersService.UpdateUsername(model.User.UserName, model.NewUsername);
					}
				}

				_userProfileService.ClearUserProfileFromCache(model.UserId);
				_userProfileService.ClearAllUserProfilesFromCache(model.Department.DepartmentId);
				_departmentsService.InvalidateDepartmentUsersInCache(model.Department.DepartmentId);
				_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
				_departmentsService.InvalidateDepartmentMembers();
				_usersService.ClearCacheForDepartment(DepartmentId);

				return RedirectToAction("Index", "Personnel", new { area = "User" });
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}
		#endregion Edit User Profile

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult DeletePushUri(int pushUriId)
		{
			if (!_authorizationService.CanUserDeletePushUri(UserId, pushUriId))
				return new UnauthorizedResult();

			var pushUri = _pushUriService.GetPushUriById(pushUriId);

			_pushService.UnRegisterNotificationOnly(pushUri);
			_pushUriService.DeleteAllPushUrisByPlatformDevice((Platforms)pushUri.PlatformType, pushUri.DeviceId);

			return RedirectToAction("Devices", "Profile", new { Area = "User" });
		}

		#region User Actions
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetCustomAction(int actionType, string note)
		{
			if (!String.IsNullOrWhiteSpace(note))
				_actionLogsService.SetUserAction(UserId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId, actionType, null, note);
			else
				_actionLogsService.SetUserAction(UserId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId, actionType);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		public IActionResult SetCustomUserAction(string userId, int actionType)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId, actionType);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetCustomStaffing(string userId, int staffingLevel)
		{
			_userStateService.CreateUserState(userId, DepartmentId, staffingLevel);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult ResetAllToStandingBy()
		{
			_actionLogsService.SetActionForEntireDepartment(_departmentsService.GetDepartmentByUserId(UserId).DepartmentId, (int)ActionTypes.StandingBy);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult ResetGroupToStandingBy(int groupId)
		{
			_actionLogsService.SetActionForDepartmentGroup(groupId, (int)ActionTypes.StandingBy);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetUserState(DashboardModel model)
		{
			int state = 0;
			if (model.CustomStaffingActive)
				state = model.UserState;
			else
				state = (int)model.UserStateEnum;

			if (String.IsNullOrWhiteSpace(model.StateNote))
				_userStateService.CreateUserState(UserId, DepartmentId, state);
			else
				_userStateService.CreateUserState(UserId, DepartmentId, state, model.StateNote);

			return RedirectToAction("Dashboard");
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult UserRespondingToStation(int stationId)
		{
			_actionLogsService.SetUserAction(UserId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
											 (int)ActionTypes.RespondingToStation, null, stationId);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult UserRespondingToCall(int callId)
		{
			if (callId > 0)
				_actionLogsService.SetUserAction(UserId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
											 (int)ActionTypes.RespondingToScene, null, callId);
			else
				_actionLogsService.SetUserAction(UserId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
											 (int)ActionTypes.RespondingToScene, null);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetStateForUser(string userId, UserStateTypes stateType)
		{
			_userStateService.CreateUserState(userId, DepartmentId, (int)stateType);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetActionForUser(string userId, int actionType)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId, actionType);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}
		#endregion User Actions

		private void Unsubscribe(string emailAddress)
		{
			try
			{
				var client = new RestClient("https://app.mailerlite.com");
				var request = new RestRequest("/api/v1/subscribers/unsubscribe/", Method.POST);
				request.AddObject(new
				{
					apiKey = "QDrnoEf6hBONlGye26aZFh5Iv1KEgdJM",
					email = emailAddress
				});
				var response = client.Execute(request);
			}
			catch { }
		}

		#region BigBoard (Deprecated)
		public IActionResult BigBoard()
		{
			return Redirect("https://bigboard.resgrid.com");
		}

		public IActionResult BigBoard2()
		{
			return Redirect("https://bigboard.resgrid.com");
		}

		public ActionResult BigBoardData()
		{
			// TODO: replace with webapi

			var serializerSettings = new JsonSerializerSettings
			{
				ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
				NullValueHandling = NullValueHandling.Include,
				//ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
				//PreserveReferencesHandling = PreserveReferencesHandling.All,
				Formatting = Formatting.Indented,
				Converters = new JsonConverter[]
				{
					new Newtonsoft.Json.Converters.IsoDateTimeConverter(),
					new Newtonsoft.Json.Converters.StringEnumConverter()
				},
			};

			var model = GetBigBoardData();

			var result = JsonConvert.SerializeObject(model, Formatting.Indented, serializerSettings);
			return Content(result, "application/json");
		}

		private Models.BigBoardX.BigBoardModel GetBigBoardData()
		{
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

			#region Calls
			var calls = _callsService.GetActiveCallsByDepartment(DepartmentId);
			var callViewModels = new List<User.Models.BigBoardX.CallViewModel>();
			foreach (var call in calls)
			{
				var callViewModel = new User.Models.BigBoardX.CallViewModel
				{
					Id = call.Number,
					Name = call.Name,
					Timestamp = call.LoggedOn.TimeConverter(department),
					LogginUser = UserHelper.GetFullNameForUser(call.ReportingUserId),
					Priority = call.ToCallPriorityDisplayText(),
					PriorityCss = call.ToCallPriorityCss(),
					State = call.ToCallStateDisplayText(),
					StateCss = call.ToCallStateCss()
				};

				callViewModels.Add(callViewModel);
			}
			#endregion

			#region Personnel
			var allUsers = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			var hideUnavailable = _departmentSettingsService.GetBigBoardHideUnavailableDepartment(DepartmentId);
			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			var departmentGroups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			var lastUserStates = _userStateService.GetLatestStatesForDepartment(DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);

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

				// if setting is such, ignore unavailable users.
				if (hideUnavailable.HasValue && hideUnavailable.Value && us.State != (int)UserStateTypes.Unavailable)
					continue;

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
			#endregion

			#region Units
			var units = _unitsService.GetUnitsForDepartment(DepartmentId);
			var unitStates = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var unitViewModels = new List<User.Models.BigBoardX.UnitViewModel>();

			var sortedUnits = from u in units
							  let station = u.StationGroup
							  let stationName = station == null ? "" : station.Name
							  orderby stationName, u.Name ascending
							  select new
							  {
								  Unit = u,
								  Station = station,
								  StationName = stationName
							  };

			foreach (var unit in sortedUnits)
			{
				var stateFound = unitStates.FirstOrDefault(x => x.UnitId == unit.Unit.UnitId);
				var state = "Unknown";
				var stateCss = "";
				int? destinationId = 0;
				decimal? latitude = 0;
				decimal? longitude = 0;

				DateTime? timestamp = null;

				if (stateFound != null)
				{
					state = stateFound.ToStateDisplayText();
					stateCss = stateFound.ToStateCss();
					destinationId = stateFound.DestinationId;
					latitude = stateFound.Latitude;
					longitude = stateFound.Longitude;
					timestamp = stateFound.Timestamp;
				}

				int groupId = 0;
				if (unit.Station != null)
					groupId = unit.Station.DepartmentGroupId;

				var unitViewModel = new User.Models.BigBoardX.UnitViewModel
				{
					Name = unit.Unit.Name,
					Type = unit.Unit.Type,
					State = state,
					StateCss = stateCss,
					Timestamp = timestamp,
					DestinationId = destinationId,
					Latitude = latitude,
					Longitude = longitude,
					GroupId = groupId,
					GroupName = unit.StationName
				};

				unitViewModels.Add(unitViewModel);
			}

			#endregion

			#region Map
			var address = _departmentSettingsService.GetBigBoardCenterAddressDepartment(DepartmentId);
			var gpsCoordinates = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(DepartmentId);

			string weatherUnits = "";
			double? centerLat = null;
			double? centerLon = null;

			if (address != null && !String.IsNullOrWhiteSpace(address.Country))
			{
				if (address.Country == "Canada")
					weatherUnits = "ca";
				else if (address.Country == "United Kingdom")
					weatherUnits = "uk";
				else if (address.Country == "Australia")
					weatherUnits = "uk";
				else
					weatherUnits = "us";
			}
			else if (department.Address != null && !String.IsNullOrWhiteSpace(department.Address.Country))
			{
				if (department.Address.Country == "Canada")
					weatherUnits = "ca";
				else if (department.Address.Country == "United Kingdom")
					weatherUnits = "uk";
				else if (department.Address.Country == "Australia")
					weatherUnits = "uk";
				else
					weatherUnits = "us";
			}

			if (!String.IsNullOrWhiteSpace(gpsCoordinates))
			{
				string[] coordinates = gpsCoordinates.Split(char.Parse(","));

				if (coordinates.Count() == 2)
				{
					double newLat;
					double newLon;
					if (double.TryParse(coordinates[0], out newLat) && double.TryParse(coordinates[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && address != null)
			{
				string coordinates = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", address.Address1,
																		address.City, address.State, address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && department.Address != null)
			{
				string coordinates = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", department.Address.Address1,
																		department.Address.City,
																		department.Address.State,
																		department.Address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue || !centerLon.HasValue)
			{
				centerLat = 39.14086268299356;
				centerLon = -119.7583809782715;
			}

			var zoomLevel = _departmentSettingsService.GetBigBoardMapZoomLevelForDepartment(department.DepartmentId);
			var refreshTime = _departmentSettingsService.GetBigBoardRefreshTimeForDepartment(department.DepartmentId);


			var mapModel = new User.Models.BigBoardX.BigBoardMapModel
			{
				CenterLat = centerLat.Value,
				CenterLon = centerLon.Value,
				ZoomLevel = zoomLevel.HasValue ? zoomLevel.Value : 9,
			};

			foreach (var station in stations)
			{
				MapMakerInfo info = new MapMakerInfo();
				info.ImagePath = "Station";
				info.Title = station.Name;
				info.InfoWindowContent = station.Name;

				if (station.Address != null)
				{
					string coordinates = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", station.Address.Address1,
																		station.Address.City,
																		station.Address.State,
																		station.Address.PostalCode));

					if (!String.IsNullOrEmpty(coordinates))
					{
						info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
						info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);

						mapModel.MapMakerInfos.Add(info);
					}
				}
				else if (!String.IsNullOrWhiteSpace(station.Latitude) && !String.IsNullOrWhiteSpace(station.Longitude))
				{
					info.Latitude = double.Parse(station.Latitude);
					info.Longitude = double.Parse(station.Longitude);

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var call in calls)
			{
				MapMakerInfo info = new MapMakerInfo();
				info.ImagePath = "Call";
				info.Title = call.Name;
				info.InfoWindowContent = call.NatureOfCall;

				if (!String.IsNullOrEmpty(call.GeoLocationData))
				{
					try
					{
						info.Latitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[0]);
						info.Longitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[1]);

						mapModel.MapMakerInfos.Add(info);
					}
					catch { }
				}
				else if (!String.IsNullOrEmpty(call.Address))
				{
					string coordinates = _geoLocationProvider.GetLatLonFromAddress(call.Address);
					if (!String.IsNullOrEmpty(coordinates))
					{
						info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
						info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);
					}

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var unit in unitStates)
			{
				if (unit.Latitude.HasValue && unit.Latitude.Value != 0 && unit.Longitude.HasValue &&
					unit.Longitude.Value != 0)
				{
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Engine_Responding";
					info.Title = unit.Unit.Name;
					info.InfoWindowContent = "";
					info.Latitude = double.Parse(unit.Latitude.Value.ToString());
					info.Longitude = double.Parse(unit.Longitude.Value.ToString());

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var person in personnelViewModels)
			{
				if (person.Latitude.HasValue && person.Latitude.Value != 0 && person.Longitude.HasValue &&
					person.Longitude.Value != 0)
				{
					MapMakerInfo info = new MapMakerInfo();

					if (person.StatusValue <= 25)
					{
						if (person.StatusValue == 5)
							info.ImagePath = "Person_RespondingStation";
						else if (person.StatusValue == 6)
							info.ImagePath = "Person_RespondingCall";
						else if (person.StatusValue == 3)
							info.ImagePath = "Person_OnScene";
						else
							info.ImagePath = "Person_RespondingCall";
					}
					else if (person.DestinationType > 0)
					{
						if (person.DestinationType == 1)
							info.ImagePath = "Person_RespondingStation";
						else if (person.DestinationType == 2)
							info.ImagePath = "Person_RespondingCall";
						else
							info.ImagePath = "Person_RespondingCall";
					}
					else
					{
						info.ImagePath = "Person_RespondingCall";
					}

					info.Title = person.Name;
					info.InfoWindowContent = "";
					info.Latitude = double.Parse(person.Latitude.Value.ToString());
					info.Longitude = double.Parse(person.Longitude.Value.ToString());

					mapModel.MapMakerInfos.Add(info);
				}
			}
			#endregion

			#region Groups
			var groupsViewModels = (from @group in departmentGroups select new GroupViewModel() { GroupId = @group.DepartmentGroupId, Name = @group.Name }).ToList();

			#endregion Groups

			var model = new User.Models.BigBoardX.BigBoardModel
			{
				Personnel = personnelViewModels,
				RefreshTime = refreshTime.HasValue ? refreshTime.Value : 5,
				MapModel = mapModel,
				Units = unitViewModels,
				Calls = callViewModels,
				WeatherUnit = weatherUnits,
				Groups = groupsViewModels
			};

			return model;
		}
		#endregion BigBoard (Deprecated)
	}
}
