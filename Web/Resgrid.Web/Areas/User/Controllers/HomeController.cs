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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Resgrid.Model.Security;

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
		private readonly IContactVerificationService _contactVerificationService;
		private readonly IUserDefinedFieldsService _userDefinedFieldsService;
		private readonly IUdfRenderingService _udfRenderingService;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> _secLocalizer;
		private readonly IGdprDataExportService _gdprDataExportService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IPhoneNumberProcesserProvider _phoneNumberProcesser;
		private readonly ISecurityPinService _securityPinService;
		private readonly IEncryptionService _encryptionService;
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;
		private readonly IUserSessionService _userSessionService;
		private readonly IDepartmentMemberEmergencyContactService _emergencyContactService;
		private readonly IProtectedReadService _protectedReadService;
		private readonly IDepartmentMemberSensitiveDataService _memberSensitiveDataService;
		private readonly IDepartmentDataProtectionService _dataProtectionService;

		public HomeController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IUserStateService userStateService, IDepartmentGroupsService departmentGroupsService, Resgrid.Model.Services.IAuthorizationService authorizationService,
			IUserProfileService userProfileService, ICallsService callsService, IGeoLocationProvider geoLocationProvider, IDepartmentSettingsService departmentSettingsService,
			IUnitsService unitsService, IAddressService addressService, IPersonnelRolesService personnelRolesService, IPushService pushService, ILimitsService limitsService,
			ICustomStateService customStateService, IEventAggregator eventAggregator, IOptions<AppOptions> appOptionsAccessor, UserManager<IdentityUser> userManager,
			IStringLocalizerFactory factory, ISubscriptionsService subscriptionsService, IContactVerificationService contactVerificationService,
			IUserDefinedFieldsService userDefinedFieldsService, IUdfRenderingService udfRenderingService, IDepartmentSsoService departmentSsoService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> secLocalizer, IGdprDataExportService gdprDataExportService,
			ISystemAuditsService systemAuditsService, IPhoneNumberProcesserProvider phoneNumberProcesser,
			ISecurityPinService securityPinService, IEncryptionService encryptionService,
			IExternalIdentityLinkService externalIdentityLinkService, IUserSessionService userSessionService,
			IDepartmentMemberEmergencyContactService emergencyContactService, IProtectedReadService protectedReadService,
			IDepartmentMemberSensitiveDataService memberSensitiveDataService,
			IDepartmentDataProtectionService dataProtectionService)
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
			_contactVerificationService = contactVerificationService;
			_userDefinedFieldsService = userDefinedFieldsService;
			_udfRenderingService = udfRenderingService;
			_departmentSsoService = departmentSsoService;
			_secLocalizer = secLocalizer;
			_gdprDataExportService = gdprDataExportService;
			_systemAuditsService = systemAuditsService;
			_phoneNumberProcesser = phoneNumberProcesser;
			_securityPinService = securityPinService;
			_encryptionService = encryptionService;
			_externalIdentityLinkService = externalIdentityLinkService;
			_userSessionService = userSessionService;
			_emergencyContactService = emergencyContactService;
			_protectedReadService = protectedReadService;
			_memberSensitiveDataService = memberSensitiveDataService;
			_dataProtectionService = dataProtectionService;

			_localizer = factory.Create("Home.Dashboard", new AssemblyName(typeof(SupportedLocales).GetTypeInfo().Assembly.FullName).Name);
		}
		#endregion Private Members and Constructors

		/// <summary>Resolves a password-error key returned by ValidatePasswordAgainstPolicyAsync into a localised message.</summary>
		private string ResolvePwdError(string key)
		{
			if (string.IsNullOrEmpty(key)) return key;
			if (key.StartsWith("PwdErrorTooShort:", StringComparison.Ordinal))
			{
				var minLen = key.Substring("PwdErrorTooShort:".Length);
				return string.Format(_secLocalizer["PwdErrorTooShort"], minLen);
			}
			return _secLocalizer[key];
		}

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
			// No userId means "my profile" -- links that come back here from a sub-page (Active
			// Sessions, Change Password) have no user to name. Without this the authorization
			// check below is handed a null target and answers 401 for the user's own profile.
			if (string.IsNullOrWhiteSpace(userId))
				userId = UserId;

			var model = new EditProfileModel();
			model.ApiUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var departmentMember = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, userId))
				return Unauthorized();

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
			await HydrateMemberIdentificationNumberAsync(model, userId);

			if (model.Profile == null)
				model.Profile = new UserProfile();

			// Security PIN is only shown to the profile's owner (never to admins editing another user).
			model.DepartmentForcesSecurityPin = await _departmentSettingsService.GetForceChatbotSecurityPinAsync(DepartmentId);
			if (model.IsOwnProfile)
			{
				model.SecurityPinEnabled = model.Profile.SecurityPinEnabled;
				model.SecurityPin = await _securityPinService.GetPinAsync(userId);
			}

			if (model.Profile.Image == null)
				model.HasCustomIamge = false;
			else
				model.HasCustomIamge = true;

			// The department-scoped address is authoritative once the member has one (plan 5.1) —
			// an address can differ per department, and only this copy is protected. The legacy
			// shared-Addresses link is read only until the contract migration clears it.
			var memberAddresses = await _memberSensitiveDataService.GetByDepartmentAndUserAsync(DepartmentId, userId);
			if (memberAddresses != null)
				await _protectedReadService.ResolveMemberSensitiveDataForReadAsync(DepartmentId, new[] { memberAddresses },
					Request.Headers["X-Resgrid-Protected-Grant"].ToString(), UserId);

			// The legacy shared-Addresses link is read ONLY while the department is unprotected and
			// relocation has not reached this member yet. Once protection is enforced that link is a
			// plaintext copy of data this department has already encrypted, and rendering it would
			// walk straight around the reveal pipeline.
			var legacyAddressFallbackAllowed = !await _dataProtectionService.IsProtectionEnforcedAsync(DepartmentId);

			if (memberAddresses != null && !string.IsNullOrWhiteSpace(memberAddresses.HomeAddress1))
			{
				model.PhysicalAddress1 = memberAddresses.HomeAddress1;
				model.PhysicalCity = memberAddresses.HomeCity;
				model.PhysicalCountry = memberAddresses.HomeCountry;
				model.PhysicalPostalCode = memberAddresses.HomePostalCode;
				model.PhysicalState = memberAddresses.HomeState;
			}
			else if (legacyAddressFallbackAllowed && model.Profile != null && model.Profile.HomeAddressId.HasValue)
			{
				var homeAddress = await _addressService.GetAddressByIdAsync(model.Profile.HomeAddressId.Value);
				model.PhysicalAddress1 = homeAddress.Address1;
				model.PhysicalCity = homeAddress.City;
				model.PhysicalCountry = homeAddress.Country;
				model.PhysicalPostalCode = homeAddress.PostalCode;
				model.PhysicalState = homeAddress.State;
			}

			if (memberAddresses != null && !string.IsNullOrWhiteSpace(memberAddresses.MailingAddress1))
			{
				model.MailingAddress1 = memberAddresses.MailingAddress1;
				model.MailingCity = memberAddresses.MailingCity;
				model.MailingCountry = memberAddresses.MailingCountry;
				model.MailingPostalCode = memberAddresses.MailingPostalCode;
				model.MailingState = memberAddresses.MailingState;

				// The department-scoped copies are independent rows, so "same as physical" is a value
				// comparison rather than the legacy shared-address-id check.
				model.MailingAddressSameAsPhysical =
					string.Equals(memberAddresses.MailingAddress1, memberAddresses.HomeAddress1, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(memberAddresses.MailingCity ?? string.Empty, memberAddresses.HomeCity ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(memberAddresses.MailingState ?? string.Empty, memberAddresses.HomeState ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(memberAddresses.MailingPostalCode ?? string.Empty, memberAddresses.HomePostalCode ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(memberAddresses.MailingCountry ?? string.Empty, memberAddresses.HomeCountry ?? string.Empty, StringComparison.OrdinalIgnoreCase);
			}
			else if (legacyAddressFallbackAllowed && model.Profile != null && model.Profile.MailingAddressId.HasValue)
			{
				if (model.Profile.HomeAddressId.HasValue &&
					model.Profile.MailingAddressId.Value == model.Profile.HomeAddressId.Value)
				{
					model.MailingAddressSameAsPhysical = true;
				}
				else
				{
					var mailingAddress = await _addressService.GetAddressByIdAsync(model.Profile.MailingAddressId.Value);
					model.MailingAddress1 = mailingAddress.Address1;
					model.MailingCity = mailingAddress.City;
					model.MailingCountry = mailingAddress.Country;
					model.MailingPostalCode = mailingAddress.PostalCode;
					model.MailingState = mailingAddress.State;
				}
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

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel, isDeptAdmin, isGroupAdmin);
				var udfValues = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Personnel, userId);
				var visibleFieldIds = udfFields.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValues = (udfValues ?? new List<UdfFieldValue>()).Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinition, udfFields, filteredValues);
			}

			var externalIdentityState = await _externalIdentityLinkService.GetSsoManagementStateAsync(userId);
			var isLegacySsoLinked = departmentMember != null &&
				(!string.IsNullOrWhiteSpace(departmentMember.ExternalSsoId) || departmentMember.SsoLinkedOn.HasValue);
			model.CanManageLocalCredentials = model.IsOwnProfile && !externalIdentityState.IsSsoManaged && !isLegacySsoLinked;
			model.IsSsoManaged = externalIdentityState.IsSsoManaged || isLegacySsoLinked;
			model.IsEmailExternallyManaged = externalIdentityState.IsEmailExternallyManaged || isLegacySsoLinked;
			model.CanResetPassword = !model.IsOwnProfile && userId != model.Department.ManagingUserId &&
				(model.Department.IsUserAnAdmin(UserId) ||
				 (group != null && group.IsUserGroupAdmin(UserId) && !model.Department.IsUserAnAdmin(userId)));
			model.RequirePasswordResetViaEmail = await _departmentSettingsService.GetRequirePasswordResetViaEmailAsync(DepartmentId);

			if (model.IsOwnProfile)
				model.ActiveDataExportRequest = await _gdprDataExportService.GetActiveRequestByUserIdAsync(userId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> EditUserProfile(EditProfileModel model, IFormCollection form, CancellationToken cancellationToken)
		{
			// Re-derive the target user identity server-side; never trust the model-bound UserId
			// if it was not explicitly provided (e.g. editing own profile via direct navigation).
			if (string.IsNullOrWhiteSpace(model.UserId))
				model.UserId = UserId;

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, model.UserId))
				return Unauthorized();

			// SECURITY: Derive IsOwnProfile server-side — never trust the form-posted value.
			// An attacker could submit IsOwnProfile=true to gain access to the password/username
			// change code paths while targeting another user's profile.
			model.IsOwnProfile = model.UserId == UserId;

			// Determine the caller's privilege level server-side for use throughout this action.
			bool callerIsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool callerIsGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);

			model.User = _usersService.GetUserById(model.UserId);
			if (model.User == null)
				return NotFound();

			var targetDepartmentMember = await _departmentsService.GetDepartmentMemberAsync(model.UserId, DepartmentId);
			var targetExternalIdentityState = await _externalIdentityLinkService.GetSsoManagementStateAsync(model.UserId, cancellationToken);
			var isLegacySsoLinkedOnPost = targetDepartmentMember != null &&
				(!string.IsNullOrWhiteSpace(targetDepartmentMember.ExternalSsoId) || targetDepartmentMember.SsoLinkedOn.HasValue);
			model.CanManageLocalCredentials = model.IsOwnProfile && !targetExternalIdentityState.IsSsoManaged && !isLegacySsoLinkedOnPost;
			model.IsSsoManaged = targetExternalIdentityState.IsSsoManaged || isLegacySsoLinkedOnPost;
			model.IsEmailExternallyManaged = targetExternalIdentityState.IsEmailExternallyManaged || isLegacySsoLinkedOnPost;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var targetGroupForPasswordReset = await _departmentGroupsService.GetGroupForUserAsync(model.UserId, DepartmentId);
			model.CanResetPassword = !model.IsOwnProfile && model.UserId != model.Department?.ManagingUserId &&
				(model.Department?.IsUserAnAdmin(UserId) == true ||
				 (targetGroupForPasswordReset != null && targetGroupForPasswordReset.IsUserGroupAdmin(UserId) &&
				  model.Department?.IsUserAnAdmin(model.UserId) != true));
			model.RequirePasswordResetViaEmail = await _departmentSettingsService.GetRequirePasswordResetViaEmailAsync(DepartmentId);
			var emailChanged = !string.Equals(model.User.Email, model.Email, StringComparison.OrdinalIgnoreCase);
			//model.PushUris = await _pushUriService.GetPushUrisByUserId(model.UserId);
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

			// Validate phone numbers to a sendable E.164 form (Twilio rejects non-E.164 'To' numbers). Use the user's
			// country as the region hint so a national-format number (e.g. "082446...") can be recognized & normalized.
			var phoneRegion = PhoneRegionHelper.ToIso(model.PhysicalCountry) ?? PhoneRegionHelper.ToIso(model.MailingCountry);
			PhoneNumberResult mobileResult = null;
			PhoneNumberResult homeResult = null;

			if (!String.IsNullOrWhiteSpace(model.Profile.MobileNumber))
			{
				mobileResult = _phoneNumberProcesser.Process(model.Profile.MobileNumber, phoneRegion);
				if (mobileResult == null || !mobileResult.IsValid)
					ModelState.AddModelError("Profile.MobileNumber", "This mobile number doesn't look valid for sending texts. Enter it in full international format, starting with your country code (for example +27 82 446 1783).");
			}

			if (!String.IsNullOrWhiteSpace(model.Profile.HomeNumber))
			{
				homeResult = _phoneNumberProcesser.Process(model.Profile.HomeNumber, phoneRegion);
				if (homeResult == null || !homeResult.IsValid)
					ModelState.AddModelError("Profile.HomeNumber", "This home/phone number doesn't look valid for calls. Enter it in full international format, starting with your country code.");
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

			if (emailChanged)
			{
				if (model.IsEmailExternallyManaged)
				{
					ModelState.AddModelError("Email", "This email address is managed by the linked SSO provider and cannot be changed in Resgrid.");
				}
				// SECURITY: Email changes are high-privilege — only the account owner or a department
				// admin may change an email address.  A group admin must NOT be able to change a
				// member's email because that enables account-takeover via the password-reset flow.
				else if (!model.IsOwnProfile && !callerIsDepartmentAdmin)
				{
					ModelState.AddModelError("Email", "You do not have permission to change this user's email address.");
				}
				else
				{
					var currentEmail = _usersService.GetUserByEmail(model.Email);

					if (currentEmail != null && currentEmail.Id != model.User.UserId.ToString())
						ModelState.AddModelError("Email", "Email Address Already in Use. Please use another one.");
				}
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
				if (!String.IsNullOrWhiteSpace(model.SecurityPin))
				{
					// Normalize once so validation sees the same value that gets encrypted on save.
					model.SecurityPin = model.SecurityPin.Trim();

					if (!SecurityPinUtility.IsValidFormat(model.SecurityPin))
						ModelState.AddModelError("SecurityPin", "The security PIN must be exactly 4 digits.");
					else if (SecurityPinUtility.IsWeak(model.SecurityPin))
						ModelState.AddModelError("SecurityPin", "That security PIN is too easy to guess. Avoid repeated digits (like 0000) and sequences (like 1234 or 4321).");
				}
			}

			if (ModelState.IsValid)
			{
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
				savedProfile.MobileNumber = (mobileResult != null && mobileResult.IsValid && !string.IsNullOrWhiteSpace(mobileResult.InternationalNumber))
					? mobileResult.InternationalNumber
					: model.Profile.MobileNumber;
				savedProfile.SendEmail = model.Profile.SendEmail;
				savedProfile.SendPush = model.Profile.SendPush;
				savedProfile.SendSms = model.Profile.SendSms;
				savedProfile.SendMessageEmail = model.Profile.SendMessageEmail;
				savedProfile.SendMessagePush = model.Profile.SendMessagePush;
				savedProfile.SendMessageSms = model.Profile.SendMessageSms;
				savedProfile.SendNotificationEmail = model.Profile.SendNotificationEmail;
				savedProfile.SendNotificationPush = model.Profile.SendNotificationPush;
				savedProfile.SendNotificationSms = model.Profile.SendNotificationSms;
				savedProfile.EnableModernApplicationSounds = model.Profile.EnableModernApplicationSounds;
				savedProfile.DoNotRecieveNewsletters = model.Profile.DoNotRecieveNewsletters;
				savedProfile.HomeNumber = (homeResult != null && homeResult.IsValid && !string.IsNullOrWhiteSpace(homeResult.InternationalNumber))
					? homeResult.InternationalNumber
					: model.Profile.HomeNumber;
				// The identification number is DEPARTMENT-SCOPED (ADP plan 5.1): a profile row is
				// global to the user, so it can neither be encrypted with one department's key nor
				// hold the different numbers different departments issue the same person. The
				// profile column is left untouched here — it is dropped in the contract migration
				// once this is deployed.
				await SaveMemberIdentificationNumberAsync(model.UserId, model.Profile.IdentificationNumber, cancellationToken);
				await SaveMemberAddressesAsync(model, cancellationToken);
				savedProfile.TimeZone = model.Profile.TimeZone;
				savedProfile.Language = model.Profile.Language;

				// Security PIN: only the profile owner can manage it. A blank input keeps the current
				// PIN; enabling the option with no PIN on file generates a random one.
				if (model.IsOwnProfile)
				{
					savedProfile.SecurityPinEnabled = model.SecurityPinEnabled;

					if (!String.IsNullOrWhiteSpace(model.SecurityPin))
						savedProfile.SecurityPin = _encryptionService.Encrypt(model.SecurityPin.Trim());
					else if (model.SecurityPinEnabled && String.IsNullOrWhiteSpace(savedProfile.SecurityPin))
						savedProfile.SecurityPin = _encryptionService.Encrypt(SecurityPinUtility.Generate());
				}

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

				if (callerIsDepartmentAdmin)
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

				// Addresses are NOT written back to the shared Addresses table or relinked on the
				// profile. SaveMemberAddressesAsync above is the only writer now (plan 5.1): the
				// department-scoped copy is the one that can be encrypted, and keeping a second
				// plaintext copy in sync would recreate exactly the leak this move exists to close.
				// The legacy link is left as it stands for members relocation has not reached yet;
				// the contract migration clears it.

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
					// SECURITY: Only department admins may change administrative flags on a member.
					// Group admins can edit profile data but must not be able to promote/demote
					// department admins, disable users, or hide them from the roster.
					if (callerIsDepartmentAdmin)
					{
						// Users Department Admin status changes, invalid the department object in cache.
						if (model.IsDepartmentAdmin != depMember.IsAdmin)
							_departmentsService.InvalidateDepartmentInCache(depMember.DepartmentId);

						depMember.IsAdmin = model.IsDepartmentAdmin;
						depMember.IsDisabled = model.IsDisabled;
						depMember.IsHidden = model.IsHidden;
					}

					await _departmentsService.SaveDepartmentMemberAsync(depMember, cancellationToken);
				}

				// Save UDF field values for personnel.
				// Detect whether the UDF section was included in this POST via the hidden "_exists" sentinel
				// keys emitted by UdfRenderingService (one per rendered field). Using the sentinel rather
				// than value-keys alone ensures we still call SaveFieldValuesForEntityAsync even when every
				// visible field was cleared to an empty string (so the service can delete existing values).
				bool udfSectionWasPosted = form.Keys.Any(k => k.StartsWith("udf_") && k.EndsWith("_exists"));

				if (udfSectionWasPosted)
				{
					var udfValues = form.Keys
						.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
						.Select(k => new UdfFieldValue
						{
							UdfFieldId = k.Substring(4),
							Value = form[k]
						}).ToList();

					bool isDeptAdmin = callerIsDepartmentAdmin;
					bool isGroupAdmin = callerIsGroupAdmin;
					var udfValidationErrors = await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Personnel, model.UserId, udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);

					if (udfValidationErrors != null && udfValidationErrors.Count > 0)
					{
						foreach (var kvp in udfValidationErrors)
						{
							foreach (var errorMessage in kvp.Value)
								ModelState.AddModelError(kvp.Key, errorMessage);
						}
					}
				}

				// Re-displays the form with the late-validation errors already in ModelState.
				async Task<IActionResult> RedisplayWithLateErrorsAsync()
				{
					var udfDefinitionOnUdfError = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel);
					if (udfDefinitionOnUdfError != null)
					{
						bool isDeptAdminOnUdfError = callerIsDepartmentAdmin;
						bool isGroupAdminOnUdfError = callerIsGroupAdmin;
						var udfFieldsOnUdfError = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel, isDeptAdminOnUdfError, isGroupAdminOnUdfError);
						var udfValuesOnUdfError = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Personnel, model.UserId);
						var visibleFieldIdsOnUdfError = udfFieldsOnUdfError.Select(f => f.UdfFieldId).ToHashSet();
						var filteredValuesOnUdfError = (udfValuesOnUdfError ?? new List<UdfFieldValue>()).Where(v => visibleFieldIdsOnUdfError.Contains(v.UdfFieldId)).ToList();
						model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionOnUdfError, udfFieldsOnUdfError, filteredValuesOnUdfError);
					}

					return View(model);
				}

				// The email change must not run until every late validation has passed: it revokes all
				// sessions and cannot be undone by returning the form with errors.
				if (!ModelState.IsValid)
					return await RedisplayWithLateErrorsAsync();

				var signedOutByEmailChange = false;
				// Email is a login/recovery identifier. Persist it through UserManager and revoke every
				// credential immediately; SSO-managed email was rejected before entering this block.
				if (emailChanged && !model.IsEmailExternallyManaged && (model.IsOwnProfile || callerIsDepartmentAdmin))
				{
					var identityUser = await _userManager.FindByIdAsync(model.User.Id);
					if (identityUser != null)
					{
						var now = DateTime.UtcNow;
						identityUser.AuthenticationGeneration++;
						identityUser.CredentialsValidAfterUtc = now;
						identityUser.AuthenticationStateChangedOn = now;
						var changeEmailResult = await _userManager.SetEmailAsync(identityUser, model.Email);
						if (changeEmailResult.Succeeded)
						{
							await _userSessionService.RevokeAllAfterCredentialChangeAsync(UserId, model.UserId,
								UserSessionRevocationReason.EmailChanged, now, cancellationToken);
							await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
							{
								System = (int)SystemAuditSystems.Website,
								Type = (int)SystemAuditTypes.EmailChanged,
								UserId = UserId,
								TargetUserId = model.UserId,
								Successful = true,
								IpAddress = IpAddressHelper.GetRequestIP(Request, true),
								ServerName = Environment.MachineName,
								CorrelationId = HttpContext.TraceIdentifier,
								Data = "Account email changed; all sessions and tokens revoked.",
								LoggedOn = now
							}, cancellationToken);
							signedOutByEmailChange = model.IsOwnProfile;
						}
						else
						{
							foreach (var error in changeEmailResult.Errors)
								ModelState.AddModelError("Email", error.Description);
						}
					}
				}

				if (!ModelState.IsValid)
					return await RedisplayWithLateErrorsAsync();


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

				if (signedOutByEmailChange)
				{
					await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					return RedirectToAction("LogOn", "Account", new { area = "", reason = "email-changed" });
				}

				return RedirectToAction("Index", "Personnel", new { area = "User" });
			}

			// If we got this far, something failed, redisplay form
			// Repopulate fields that are not round-tripped via the form
			var udfDefinitionOnFailure = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel);
			if (udfDefinitionOnFailure != null)
			{
				bool isDeptAdminOnFailure = callerIsDepartmentAdmin;
				bool isGroupAdminOnFailure = callerIsGroupAdmin;
				var udfFieldsOnFailure = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Personnel, isDeptAdminOnFailure, isGroupAdminOnFailure);
				var udfValuesOnFailure = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Personnel, model.UserId);
				var visibleFieldIdsOnFailure = udfFieldsOnFailure.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValuesOnFailure = (udfValuesOnFailure ?? new List<UdfFieldValue>()).Where(v => visibleFieldIdsOnFailure.Contains(v.UdfFieldId)).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionOnFailure, udfFieldsOnFailure, filteredValuesOnFailure);
			}

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

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetCustomUserAction(string userId, int actionType)
		{
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

			if (member == null)
				return Unauthorized();

			if (userId != UserId && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, actionType);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetCustomStaffing(string userId, int staffingLevel)
		{
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

			if (member == null)
				return Unauthorized();

			if (userId != UserId && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			await _userStateService.CreateUserState(userId, DepartmentId, staffingLevel);

			return new StatusCodeResult((int)HttpStatusCode.NoContent);
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ResetAllToStandingBy()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			await _actionLogsService.SetActionForEntireDepartmentAsync((await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId, (int)ActionTypes.StandingBy, String.Empty);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ResetGroupToStandingBy(int groupId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && !ClaimsAuthorizationHelper.IsUserGroupAdmin(groupId))
				return Unauthorized();

			var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				return Unauthorized();

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

		#region Contact Verification (AJAX)
		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SendContactVerificationCode([FromBody] SendContactVerificationCodeRequest request, CancellationToken cancellationToken)
		{
			if (request == null || !Enum.IsDefined(typeof(ContactVerificationType), request.Type))
				return BadRequest();

			ContactVerificationSendStatus sendStatus;
			string departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);

			switch (request.Type)
			{
				case ContactVerificationType.Email:
					sendStatus = await _contactVerificationService.SendEmailVerificationCodeAsync(UserId, DepartmentId, cancellationToken);
					break;
				case ContactVerificationType.MobileNumber:
					sendStatus = await _contactVerificationService.SendMobileVerificationCodeAsync(UserId, DepartmentId, departmentNumber, cancellationToken);
					break;
				case ContactVerificationType.HomeNumber:
					sendStatus = await _contactVerificationService.SendHomeVerificationCodeAsync(UserId, DepartmentId, departmentNumber, cancellationToken);
					break;
				default:
					return BadRequest();
			}

			return Json(new
			{
				success = sendStatus == ContactVerificationSendStatus.Sent,
				errorCode = sendStatus == ContactVerificationSendStatus.Sent ? null : sendStatus.ToString()
			});
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ConfirmContactVerificationCode([FromBody] ConfirmContactVerificationCodeRequest request, CancellationToken cancellationToken)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Code))
				return BadRequest();

			// Use the X-Forwarded-For aware helper so the audit log records the real client IP
			// rather than the reverse-proxy / load-balancer address.
			string ipAddress = IpAddressHelper.GetRequestIP(Request, true);
			bool confirmed = await _contactVerificationService.ConfirmVerificationCodeAsync(UserId, DepartmentId, request.Type, request.Code, ipAddress, cancellationToken);

			return Json(new { success = confirmed });
		}

		/// <summary>
		/// AJAX: validates a phone number the user is entering on the profile page and returns its canonical E.164
		/// form. Read-only (no state change), so it intentionally skips antiforgery. The profile form calls this on
		/// blur to warn about — and offer a one-click fix for — numbers Twilio would reject.
		/// </summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult ValidatePhoneNumber([FromBody] ValidatePhoneNumberRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Number))
				return Json(new { isValid = false, formatted = (string)null, message = "Please enter a phone number." });

			var iso = PhoneRegionHelper.ToIso(request.Country);
			var result = _phoneNumberProcesser.Process(request.Number, iso);

			if (result != null && result.IsValid && !string.IsNullOrWhiteSpace(result.InternationalNumber))
				return Json(new { isValid = true, formatted = result.InternationalNumber, message = (string)null });

			return Json(new
			{
				isValid = false,
				formatted = (result != null && !string.IsNullOrWhiteSpace(result.InternationalNumber)) ? result.InternationalNumber : null,
				message = "This number doesn't look valid for sending. Enter it in full international format, starting with your country code (for example +27 82 446 1783)."
			});
		}

		public sealed class ValidatePhoneNumberRequest
		{
			public string Number { get; set; }
			public string Country { get; set; }
		}
		#endregion Contact Verification (AJAX)

		#region GDPR Data Export

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> RequestMyData(CancellationToken cancellationToken)
		{
			var existing = await _gdprDataExportService.GetActiveRequestByUserIdAsync(UserId);
			if (existing != null)
			{
				TempData["GdprError"] = "You already have a pending or in-progress data export request. Please wait for it to complete.";
				return RedirectToAction("EditUserProfile", new { userId = UserId });
			}

			await _gdprDataExportService.CreateExportRequestAsync(UserId, DepartmentId, cancellationToken);

			var user = _usersService.GetUserById(UserId);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.GdprDataExportRequested,
				DepartmentId = DepartmentId,
				UserId = UserId,
				Username = user?.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"GDPR data export requested. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			TempData["GdprSuccess"] = "Your data export request has been submitted. You will receive an email when it is ready.";

			return RedirectToAction("EditUserProfile", new { userId = UserId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> DownloadMyData(string token, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(token))
				return NotFound();

			var request = await _gdprDataExportService.GetRequestByTokenAsync(token);
			if (request == null)
				return NotFound();

			if (request.UserId != UserId)
				return Forbid();

			if (request.TokenExpiresAt.HasValue && request.TokenExpiresAt.Value < DateTime.UtcNow)
			{
				TempData["GdprError"] = "This download link has expired. Please submit a new data export request.";
				return RedirectToAction("EditUserProfile", new { userId = UserId });
			}

			if (request.ExportData == null || request.ExportData.Length == 0)
				return NotFound();

			// Hold a reference to the data before invalidating the record
			var fileData = request.ExportData;
			var fileName = $"resgrid_data_export_{DateTime.UtcNow:yyyyMMdd}.zip";

			// Invalidate the token so this is a one-time download
			await _gdprDataExportService.MarkDownloadedAsync(request, cancellationToken);

			var user = _usersService.GetUserById(UserId);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.GdprDataExportDownloaded,
				DepartmentId = DepartmentId,
				UserId = UserId,
				Username = user?.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"GDPR data export downloaded (file: {fileName}). {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			return File(fileData, "application/zip", fileName);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> GetDataExportStatus()
		{
			var request = await _gdprDataExportService.GetActiveRequestByUserIdAsync(UserId);
			if (request == null)
				return Json(new { status = "none" });

			return Json(new { status = request.Status, statusName = ((GdprExportStatus)request.Status).ToString() });
		}

		#endregion GDPR Data Export

		#region Emergency contacts

		/// <summary>
		/// A member's department-scoped emergency contacts. Authorization reuses
		/// CanUserEditProfileAsync — the same rule the rest of this page runs on: a member manages
		/// their own, a department admin (or a group admin over that member) manages anyone's.
		/// Protected departments resolve through the read pipeline, so values arrive as plaintext
		/// with a valid grant and as the REDACTED placeholder without one — never as ciphertext.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> GetEmergencyContacts(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				userId = UserId;

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, userId))
				return Unauthorized();

			var contacts = await _emergencyContactService.GetAllForMemberAsync(DepartmentId, userId);

			await _protectedReadService.ResolveMemberEmergencyContactsForReadAsync(DepartmentId, contacts,
				Request.Headers["X-Resgrid-Protected-Grant"].ToString(), UserId);

			return Json(contacts.Select(c => new
			{
				id = c.DepartmentMemberEmergencyContactId,
				name = c.Name,
				relationship = c.Relationship,
				phoneNumber = c.PhoneNumber,
				alternatePhoneNumber = c.AlternatePhoneNumber,
				email = c.Email,
				notes = c.Notes,
				isPrimary = c.IsPrimary,
				sortOrder = c.SortOrder
			}));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveEmergencyContact([FromForm] EmergencyContactInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var targetUserId = string.IsNullOrWhiteSpace(input.UserId) ? UserId : input.UserId;

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, targetUserId))
				return Unauthorized();

			if (string.IsNullOrWhiteSpace(input.Name))
				return Json(new { success = false, error = "name_required" });

			DepartmentMemberEmergencyContact contact;
			if (input.Id > 0)
			{
				// Load through the member-scoped accessor so an id from another member (or another
				// department) can never be edited by guessing it.
				var existing = await _emergencyContactService.GetAllForMemberAsync(DepartmentId, targetUserId);
				contact = existing.FirstOrDefault(x => x.DepartmentMemberEmergencyContactId == input.Id);

				if (contact == null)
					return Json(new { success = false, error = "not_found" });
			}
			else
			{
				contact = new DepartmentMemberEmergencyContact
				{
					DepartmentId = DepartmentId,
					UserId = targetUserId,
					CreatedByUserId = UserId
				};
			}

			contact.Name = input.Name;
			contact.Relationship = input.Relationship;
			contact.PhoneNumber = input.PhoneNumber;
			contact.AlternatePhoneNumber = input.AlternatePhoneNumber;
			contact.Email = input.Email;
			contact.Notes = input.Notes;
			contact.IsPrimary = input.IsPrimary;
			contact.SortOrder = input.SortOrder;
			contact.UpdatedByUserId = UserId;

			var saved = await _emergencyContactService.SaveAsync(contact, cancellationToken);

			return Json(new { success = true, id = saved.DepartmentMemberEmergencyContactId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteEmergencyContact([FromForm] int id, [FromForm] string userId,
			CancellationToken cancellationToken)
		{
			var targetUserId = string.IsNullOrWhiteSpace(userId) ? UserId : userId;

			if (!await _authorizationService.CanUserEditProfileAsync(UserId, DepartmentId, targetUserId))
				return Unauthorized();

			// The service scopes the delete by department AND user, so a stray id cannot reach
			// another member's row even past the check above.
			var deleted = await _emergencyContactService.DeleteAsync(id, DepartmentId, targetUserId, UserId, cancellationToken);

			return Json(new { success = deleted });
		}

		public class EmergencyContactInput
		{
			public int Id { get; set; }
			public string UserId { get; set; }
			public string Name { get; set; }
			public string Relationship { get; set; }
			public string PhoneNumber { get; set; }
			public string AlternatePhoneNumber { get; set; }
			public string Email { get; set; }
			public string Notes { get; set; }
			public bool IsPrimary { get; set; }
			public int SortOrder { get; set; }
		}

		#endregion Emergency contacts


		/// <summary>
		/// Loads the member's department-scoped identification number onto the profile view model.
		/// Protected departments resolve it through the read pipeline, so it arrives as plaintext
		/// with a valid grant and as the REDACTED placeholder without one — never as ciphertext.
		/// </summary>
		private async Task HydrateMemberIdentificationNumberAsync(EditProfileModel model, string userId)
		{
			if (model?.Profile == null)
				return;

			var sensitive = await _memberSensitiveDataService.GetByDepartmentAndUserAsync(DepartmentId, userId);
			if (sensitive == null)
			{
				model.Profile.IdentificationNumber = null;
				return;
			}

			await _protectedReadService.ResolveMemberSensitiveDataForReadAsync(DepartmentId, new[] { sensitive },
				Request.Headers["X-Resgrid-Protected-Grant"].ToString(), UserId);

			model.Profile.IdentificationNumber = sensitive.IdentificationNumber;
		}

		/// <summary>
		/// Persists the member's department-scoped home and mailing addresses (plan 5.1). Values
		/// still showing the REDACTED placeholder were never revealed to this user and are skipped
		/// rather than written back over the stored address.
		/// </summary>
		private async Task SaveMemberAddressesAsync(EditProfileModel model, CancellationToken cancellationToken)
		{
			if (model == null)
				return;

			string Submitted(string value) => value == ProtectedDataEnvelope.RedactionValue ? null : value;

			var home1 = Submitted(model.PhysicalAddress1);
			var mailing1 = Submitted(model.MailingAddressSameAsPhysical ? model.PhysicalAddress1 : model.MailingAddress1);

			var sensitive = await _memberSensitiveDataService.GetByDepartmentAndUserAsync(DepartmentId, model.UserId);
			if (sensitive == null)
			{
				if (string.IsNullOrWhiteSpace(home1) && string.IsNullOrWhiteSpace(mailing1))
					return;

				sensitive = new DepartmentMemberSensitiveData { DepartmentId = DepartmentId, UserId = model.UserId };
			}

			sensitive.HomeAddress1 = home1;
			sensitive.HomeCity = Submitted(model.PhysicalCity);
			sensitive.HomeState = Submitted(model.PhysicalState);
			sensitive.HomePostalCode = Submitted(model.PhysicalPostalCode);
			sensitive.HomeCountry = Submitted(model.PhysicalCountry);

			// "Same as physical" stores a copy rather than a shared reference: these columns are
			// encrypted per row, so there is nothing to share and a later edit to one must not
			// silently rewrite the other.
			sensitive.MailingAddress1 = mailing1;
			sensitive.MailingCity = Submitted(model.MailingAddressSameAsPhysical ? model.PhysicalCity : model.MailingCity);
			sensitive.MailingState = Submitted(model.MailingAddressSameAsPhysical ? model.PhysicalState : model.MailingState);
			sensitive.MailingPostalCode = Submitted(model.MailingAddressSameAsPhysical ? model.PhysicalPostalCode : model.MailingPostalCode);
			sensitive.MailingCountry = Submitted(model.MailingAddressSameAsPhysical ? model.PhysicalCountry : model.MailingCountry);

			await _memberSensitiveDataService.SaveAsync(sensitive, cancellationToken);
		}

		/// <summary>
		/// Persists the member's department-scoped identification number, creating the row on first
		/// use. A value still showing the REDACTED placeholder was never revealed to this user, so it
		/// is ignored rather than written back over the stored value.
		/// </summary>
		private async Task SaveMemberIdentificationNumberAsync(string userId, string identificationNumber,
			CancellationToken cancellationToken)
		{
			if (identificationNumber == ProtectedDataEnvelope.RedactionValue)
				return;

			var sensitive = await _memberSensitiveDataService.GetByDepartmentAndUserAsync(DepartmentId, userId);

			if (sensitive == null)
			{
				if (string.IsNullOrWhiteSpace(identificationNumber))
					return;

				sensitive = new DepartmentMemberSensitiveData { DepartmentId = DepartmentId, UserId = userId };
			}

			sensitive.IdentificationNumber = identificationNumber;

			await _memberSensitiveDataService.SaveAsync(sensitive, cancellationToken);
		}

	}

	/// <summary>Request body for sending a contact verification code.</summary>
	public sealed class SendContactVerificationCodeRequest
	{
		public ContactVerificationType Type { get; set; }
	}

	/// <summary>Request body for confirming a contact verification code.</summary>
	public sealed class ConfirmContactVerificationCodeRequest
	{
		public ContactVerificationType Type { get; set; }
		public string Code { get; set; }
	}
}
