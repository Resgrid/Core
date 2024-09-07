using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
using Resgrid.Model.Identity;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Microsoft.Extensions.Localization;
using System.Reflection;
using Resgrid.Localization;
using Microsoft.AspNetCore.Localization;
using System.Web;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]

	[ClaimsResource(ResgridClaimTypes.Resources.Department)]
	public class HomeController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IStringLocalizer _localizer;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
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
		private readonly UserManager<IdentityUser> _userManager;
		private readonly ISubscriptionsService _subscriptionsService;

		public HomeController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IUserStateService userStateService, IDepartmentGroupsService departmentGroupsService, Resgrid.Model.Services.IAuthorizationService authorizationService,
			IUserProfileService userProfileService, ICallsService callsService, IGeoLocationProvider geoLocationProvider, IDepartmentSettingsService departmentSettingsService,
			IUnitsService unitsService, IAddressService addressService, IPersonnelRolesService personnelRolesService, IPushService pushService, ILimitsService limitsService,
			ICustomStateService customStateService, IEventAggregator eventAggregator, IOptions<AppOptions> appOptionsAccessor, UserManager<IdentityUser> userManager,
			IStringLocalizerFactory factory, ISubscriptionsService subscriptionsService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
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
			_subscriptionsService = subscriptionsService;

			_localizer = factory.Create("Home.Dashboard", new AssemblyName(typeof(SupportedLocales).GetTypeInfo().Assembly.FullName).Name);
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Dashboard(bool firstRun = false)
		{
			var model = new DashboardModel();

			var staffingLevel = await _userStateService.GetLastUserStateByUserIdAsync(UserId);
			model.UserState = staffingLevel.State;
			//model.StateNote = staffingLevel.Note;

			var staffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
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

			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.FirstRun = firstRun;
			model.Number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			model.States = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);

			return View(model);
		}

		#region Partials
		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetUserStatusTable()
		{
			var model = new UserStatusTableModel();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.LastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			model.UserStates = new List<UserState>();
			model.DepartmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			model.UsersGroup = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			model.States = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			model.StaffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			var personnelStatusSortOrder = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			model.ExcludedUsers = await _departmentsService.GetAllDisabledOrHiddenUsersAsync(DepartmentId);

			List<string> groupedUserIds = new List<string>();

			foreach (var dg in model.DepartmentGroups)
			{
				UserStatusGroup group = new UserStatusGroup();
				group.Group = dg;

				var membersToProcess = from member in dg.Members
									   where !(model.ExcludedUsers.Any(item2 => item2 == member.UserId))
									   select member;

				foreach (var u in membersToProcess)
				{
					if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.UserId, UserId, DepartmentId))
						continue;

					if (allUsers.Any(x => x.UserId == u.UserId))
					{
						groupedUserIds.Add(u.UserId);
						var userInfo = allUsers.FirstOrDefault(x => x.UserId == u.UserId);

						UserState state = userStates.FirstOrDefault(x => x.UserId == u.UserId);

						if (state == null)
						{
							state = new UserState();
							state.UserId = u.UserId;
							state.AutoGenerated = true;
							state.Timestamp = DateTime.UtcNow;
							state.State = (int)UserStateTypes.Available;
						}

						if (!model.DepartmentUserStates.ContainsKey(u.UserId))
							model.DepartmentUserStates.Add(u.UserId, state);

						var al = model.LastUserActionlogs.FirstOrDefault(x => x.UserId == u.UserId);

						UserStatus userStatus = new UserStatus();
						userStatus.UserInfo = userInfo;
						userStatus.CurrentStatus = al;
						userStatus.CurrentStaffing = state;

						if (al != null)
						{
							if (personnelStatusSortOrder != null && personnelStatusSortOrder.Any())
							{
								var statusSorting = personnelStatusSortOrder.FirstOrDefault(x => x.StatusId == al.ActionTypeId);
								if (statusSorting != null)
									userStatus.Weight = statusSorting.Weight;
								else
									userStatus.Weight = 9000;
							}
							else
							{
								userStatus.Weight = 9000;
							}
						}
						else
							userStatus.Weight = 9000;

						group.UserStatuses.Add(userStatus);
					}
				}

				switch (personnelSortOrder)
				{
					case PersonnelSortOrders.Default:
						group.UserStatuses = group.UserStatuses.OrderBy(x => x.Weight).ToList();
						break;
					case PersonnelSortOrders.FirstName:
						group.UserStatuses = group.UserStatuses.OrderBy(x => x.Weight).ThenBy(x => x.UserInfo.FirstName).ToList();
						break;
					case PersonnelSortOrders.LastName:
						group.UserStatuses = group.UserStatuses.OrderBy(x => x.Weight).ThenBy(x => x.UserInfo.LastName).ToList();
						break;
					default:
						group.UserStatuses = group.UserStatuses.OrderBy(x => x.Weight).ToList();
						break;
				}

				model.UserStatusGroups.Add(group);

				var allGroupMembers = new List<DepartmentGroupMember>(dg.Members);
			}

			var ungroupedUsers = from u in allUsers
								 where !(groupedUserIds.Contains(u.UserId)) && !(model.ExcludedUsers.Any(item2 => item2 == u.UserId))
								 select u;

			UserStatusGroup unGroupedUsersGroup = new UserStatusGroup();
			unGroupedUsersGroup.Group = null;
			foreach (var u in ungroupedUsers)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.UserId, UserId, DepartmentId))
					continue;

				model.UnGroupedUsers.Add(u.UserId);

				UserState state = userStates.FirstOrDefault(x => x.UserId == u.UserId);
				var userInfo = allUsers.FirstOrDefault(x => x.UserId == u.UserId);

				if (state == null)
				{
					state = new UserState();
					state.UserId = u.UserId;
					state.AutoGenerated = true;
					state.Timestamp = DateTime.UtcNow;
					state.State = (int)UserStateTypes.Available;
				}

				var al = model.LastUserActionlogs.FirstOrDefault(x => x.UserId == u.UserId);

				UserStatus userStatus = new UserStatus();
				userStatus.UserInfo = userInfo;
				userStatus.CurrentStatus = al;
				userStatus.CurrentStaffing = state;

				if (al != null)
				{
					if (personnelStatusSortOrder != null && personnelStatusSortOrder.Any())
					{
						var statusSorting = personnelStatusSortOrder.FirstOrDefault(x => x.StatusId == al.ActionTypeId);
						if (statusSorting != null)
							userStatus.Weight = statusSorting.Weight;
						else
							userStatus.Weight = 9000;
					}
					else
					{
						userStatus.Weight = 9000;
					}
				}
				else
					userStatus.Weight = 9000;

				unGroupedUsersGroup.UserStatuses.Add(userStatus);
			}

			switch (personnelSortOrder)
			{
				case PersonnelSortOrders.Default:
					unGroupedUsersGroup.UserStatuses = unGroupedUsersGroup.UserStatuses.OrderBy(x => x.Weight).ToList();
					break;
				case PersonnelSortOrders.FirstName:
					unGroupedUsersGroup.UserStatuses = unGroupedUsersGroup.UserStatuses.OrderBy(x => x.Weight).ThenBy(x => x.UserInfo.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					unGroupedUsersGroup.UserStatuses = unGroupedUsersGroup.UserStatuses.OrderBy(x => x.Weight).ThenBy(x => x.UserInfo.LastName).ToList();
					break;
				default:
					unGroupedUsersGroup.UserStatuses = unGroupedUsersGroup.UserStatuses.OrderBy(x => x.Weight).ToList();
					break;
			}
			model.UserStatusGroups.Add(unGroupedUsersGroup);

			return PartialView("_UserStatusTablePartial", model);
		}

		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> UserActionsPartial()
		{
			var model = new UserActionsPartialView();
			model.States = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);

			return View("_UserActionsPartial", model);
		}

		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> PersonnelActionButtonsPartial()
		{
			var model = new PersonnelActionButtonsPartialView();
			model.States = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			model.StaffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);

			return View("_PersonnelActionButtonsPartial", model);
		}
		#endregion Partials

		#region Edit User Profile
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> EditUserProfile(string userId)
		{
			var model = new EditProfileModel();
			model.ApiUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var departmentMember = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, userId))
				Unauthorized();

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Department.DepartmentId));

			ViewBag.Carriers = model.Carrier.ToSelectList().OrderBy(x => x.Text);
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");

			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, DepartmentId);

			if (group != null)
			{
				model.UserGroup = group.DepartmentGroupId;
				model.IsUserGroupAdmin = group.IsUserGroupAdmin(userId);
			}

			//model.UsersRoles = await _personnelRolesService.GetRolesForUser(userId);
			model.IsDisabled = departmentMember.IsDisabled.HasValue != false && departmentMember.IsDisabled.Value;
			model.IsHidden = departmentMember.IsHidden.HasValue != false && departmentMember.IsHidden.Value;
			model.IsDepartmentAdmin = departmentMember.IsAdmin.HasValue != false && departmentMember.IsAdmin.Value;
			model.CanEnableVoice = await _limitsService.CanDepartmentUseVoiceAsync(DepartmentId);

			if (userId == UserId)
				model.IsOwnProfile = true;

			model.User = _usersService.GetUserById(userId, true);
			model.UserId = userId;
			model.Email = model.User.Email;

			model.Profile = await _userProfileService.GetProfileByUserIdAsync(userId, true);

			if (model.Profile == null)
				model.Profile = new UserProfile();

			if (model.Profile.Image == null)
				model.HasCustomIamge = false;
			else
				model.HasCustomIamge = true;

			if (model.Profile != null && model.Profile.HomeAddressId.HasValue)
			{
				var homeAddress = await _addressService.GetAddressByIdAsync(model.Profile.HomeAddressId.Value);
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
				var mailingAddress = await _addressService.GetAddressByIdAsync(model.Profile.MailingAddressId.Value);
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

				var userProfile = await _userProfileService.GetProfileByUserIdAsync(userId);

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

			model.EnableSms = model.Profile.SendSms;
			var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);

			if (payment != null)
				model.IsFreePlan = payment.IsFreePlan();

			if (String.IsNullOrWhiteSpace(model.Profile.Language))
				model.Profile.Language = "en";

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> EditUserProfile(EditProfileModel model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, model.UserId))
				Unauthorized();

			model.User = _usersService.GetUserById(model.UserId);
			//model.PushUris = await _pushUriService.GetPushUrisByUserId(model.UserId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CanEnableVoice = await _limitsService.CanDepartmentUseVoiceAsync(DepartmentId);

			var groups = new List<DepartmentGroup>();
			var defaultGroup = new DepartmentGroup();
			defaultGroup.Name = "No Group";
			groups.Add(defaultGroup);
			groups.AddRange(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Department.DepartmentId));
			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");

			ViewBag.Carriers = model.Carrier.ToSelectList();
			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Languages = new SelectList(SupportedLocales.SupportedLanguagesMap, "Key", "Value");

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
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

				var savedProfile = await _userProfileService.GetProfileByUserIdAsync(model.UserId);

				if (savedProfile == null)
					savedProfile = new UserProfile();

				auditEvent.Before = savedProfile.CloneJsonToString();

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
				savedProfile.Language = model.Profile.Language;

				if (model.CanEnableVoice)
				{
					savedProfile.VoiceForCall = model.Profile.VoiceForCall;

					if (savedProfile.VoiceForCall)
					{
						savedProfile.VoiceCallHome = model.Profile.VoiceCallHome;
						savedProfile.VoiceCallMobile = model.Profile.VoiceCallMobile;
					}
					else
					{
						savedProfile.VoiceCallHome = false;
						savedProfile.VoiceCallMobile = false;
					}
				}
				else
				{
					savedProfile.VoiceForCall = false;
					savedProfile.VoiceCallHome = false;
					savedProfile.VoiceCallMobile = false;
				}

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				{
					var currentGroup = await _departmentGroupsService.GetGroupForUserAsync(model.UserId, DepartmentId);
					if (model.UserGroup != 0 && (currentGroup == null || currentGroup.DepartmentGroupId != model.UserGroup))
						await _departmentGroupsService.MoveUserIntoGroupAsync(model.UserId, model.UserGroup, model.IsUserGroupAdmin, DepartmentId, cancellationToken);
					else if (currentGroup != null && currentGroup.DepartmentGroupId == model.UserGroup)
					{
						var member = await _departmentGroupsService.GetGroupMemberForUserAsync(model.UserId, DepartmentId);

						if (member != null)
						{
							member.IsAdmin = model.IsUserGroupAdmin;
							_departmentGroupsService.SaveGroupMember(member);
						}
					}
					else if (model.UserGroup <= 0)
						await _departmentGroupsService.DeleteUserFromGroupsAsync(model.UserId, DepartmentId, cancellationToken);
				}

				if (form.ContainsKey("roles"))
				{
					var roles = form["roles"].ToString().Split(char.Parse(","));

					if (roles.Any())
						await _personnelRolesService.SetRolesForUserAsync(DepartmentId, model.UserId, roles, cancellationToken);
				}

				if (savedProfile.HomeAddressId.HasValue)
					homeAddress = await _addressService.GetAddressByIdAsync(savedProfile.HomeAddressId.Value);

				if (savedProfile.MailingAddressId.HasValue)
					mailingAddress = await _addressService.GetAddressByIdAsync(savedProfile.MailingAddressId.Value);

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

					homeAddress = await _addressService.SaveAddressAsync(homeAddress, cancellationToken);
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

					mailingAddress = await _addressService.SaveAddressAsync(mailingAddress, cancellationToken);
					savedProfile.MailingAddressId = mailingAddress.AddressId;
				}

				if (model.IsFreePlan)
				{
					savedProfile.SendSms = false;
					savedProfile.SendMessageSms = false;
				}

				savedProfile.LastUpdated = DateTime.UtcNow;
				await _userProfileService.SaveProfileAsync(DepartmentId, savedProfile, cancellationToken);

				auditEvent.After = savedProfile.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				var depMember = await _departmentsService.GetDepartmentMemberAsync(model.UserId, DepartmentId);
				if (depMember != null)
				{
					// Users Department Admin status changes, invalid the department object in cache.
					if (model.IsDepartmentAdmin != depMember.IsAdmin)
						_departmentsService.InvalidateDepartmentInCache(depMember.DepartmentId);

					depMember.IsAdmin = model.IsDepartmentAdmin;
					depMember.IsDisabled = model.IsDisabled;
					depMember.IsHidden = model.IsHidden;

					await _departmentsService.SaveDepartmentMemberAsync(depMember, cancellationToken);
				}

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
						await _usersService.UpdateUsername(model.User.UserName, model.NewUsername);
					}
				}

				_userProfileService.ClearUserProfileFromCache(model.UserId);
				_userProfileService.ClearAllUserProfilesFromCache(model.Department.DepartmentId);
				_departmentsService.InvalidateDepartmentUsersInCache(model.Department.DepartmentId);
				_departmentsService.InvalidatePersonnelNamesInCache(DepartmentId);
				_departmentsService.InvalidateDepartmentMembers();
				_usersService.ClearCacheForDepartment(DepartmentId);

				if (!String.IsNullOrWhiteSpace(savedProfile.Language))
				{
					Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(savedProfile.Language)), new CookieOptions { Expires = DateTime.UtcNow.AddYears(1) });
					// This guy I think is causing issues with like DateTime rendering mm/dd/yy vs dd/mm/yy, so need to look into that more. -SJ
					//Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(savedProfile.Language);
					Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(savedProfile.Language);
				}

				return RedirectToAction("Index", "Personnel", new { area = "User" });
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}
		#endregion Edit User Profile


		#region User Actions
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetCustomAction(int actionType, string note)
		{
			if (!String.IsNullOrWhiteSpace(note))
				await _actionLogsService.SetUserActionAsync(UserId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, actionType, null, note);
			else
				await _actionLogsService.SetUserActionAsync(UserId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, actionType);

			return new StatusCodeResult((int)HttpStatusCode.NoContent);
		}

		public async Task<IActionResult> SetCustomUserAction(string userId, int actionType)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, actionType);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetCustomStaffing(string userId, int staffingLevel)
		{
			await _userStateService.CreateUserState(userId, DepartmentId, staffingLevel);

			return new StatusCodeResult((int)HttpStatusCode.NoContent);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ResetAllToStandingBy()
		{
			await _actionLogsService.SetActionForEntireDepartmentAsync((await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, (int)ActionTypes.StandingBy, String.Empty);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ResetGroupToStandingBy(int groupId)
		{
			await _actionLogsService.SetActionForDepartmentGroupAsync(groupId, (int)ActionTypes.StandingBy, String.Empty);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetUserState(DashboardModel model)
		{
			int state = 0;
			if (model.CustomStaffingActive)
				state = model.UserState;
			else
				state = (int)model.UserStateEnum;

			if (String.IsNullOrWhiteSpace(model.StateNote))
				await _userStateService.CreateUserState(UserId, DepartmentId, state);
			else
				await _userStateService.CreateUserState(UserId, DepartmentId, state, model.StateNote);

			return RedirectToAction("Dashboard");
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> UserRespondingToStation(int stationId)
		{
			await _actionLogsService.SetUserActionAsync(UserId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
											 (int)ActionTypes.RespondingToStation, null, stationId);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> UserRespondingToCall(int callId)
		{
			if (callId > 0)
				await _actionLogsService.SetUserActionAsync(UserId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
											 (int)ActionTypes.RespondingToScene, null, callId);
			else
				await _actionLogsService.SetUserActionAsync(UserId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
											 (int)ActionTypes.RespondingToScene, null);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetStateForUser(string userId, UserStateTypes stateType)
		{
			await _userStateService.CreateUserState(userId, DepartmentId, (int)stateType);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetActionForUser(string userId, int actionType)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, actionType);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}
		#endregion User Actions
	}
}
