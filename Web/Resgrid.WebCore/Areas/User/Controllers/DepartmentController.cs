using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Areas.User.Models.Departments.CallSettings;
using Resgrid.Web.Areas.User.Models.Departments.Text;
using Resgrid.Web.Areas.User.Models.Departments.UnitSettings;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Areas.User.Models.Settings;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Web.Models.AccountViewModels;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class DepartmentController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IEmailService _emailService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDeleteService _deleteService;
		private readonly IInvitesService _invitesService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IAddressService _addressService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly ILimitsService _limitsService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitsService _unitsService;
		private readonly ICertificationService _certificationService;
		private readonly INumbersService _numbersService;
		private readonly IScheduledTasksService _scheduledTasksService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly ICqrsProvider _cqrsProvider;

		public DepartmentController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IUserProfileService userProfileService, IDeleteService deleteService,
			IInvitesService invitesService, Model.Services.IAuthorizationService authorizationService, IAddressService addressService, ISubscriptionsService subscriptionsService,
			ILimitsService limitsService, ICallsService callsService, IDepartmentSettingsService departmentSettingsService, IUnitsService unitsService,
			ICertificationService certificationService, INumbersService numbersService, IScheduledTasksService scheduledTasksService, IPersonnelRolesService personnelRolesService,
			IEventAggregator eventAggregator, ICustomStateService customStateService, ICqrsProvider cqrsProvider)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_emailService = emailService;
			_departmentGroupsService = departmentGroupsService;
			_userProfileService = userProfileService;
			_deleteService = deleteService;
			_invitesService = invitesService;
			_authorizationService = authorizationService;
			_addressService = addressService;
			_subscriptionsService = subscriptionsService;
			_limitsService = limitsService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
			_unitsService = unitsService;
			_certificationService = certificationService;
			_numbersService = numbersService;
			_scheduledTasksService = scheduledTasksService;
			_personnelRolesService = personnelRolesService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_cqrsProvider = cqrsProvider;
		}
		#endregion Private Members and Constructors

		#region Invites
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Invites()
		{
			var model = new InvitesView();
			model.Invites = _invitesService.GetAllInvitesForDepartment(DepartmentId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			if (model.Invites == null)
				model.Invites = new List<Invite>();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Invites(InvitesView model)
		{
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			List<string> emails = new List<string>();
			string[] rawEmails = model.EmailAddresses.Split(char.Parse(","));

			foreach (var s in rawEmails)
			{
				if (!String.IsNullOrEmpty(s.Trim()))
					emails.Add(s.Trim());
			}

			foreach (var email in emails)
			{
				if (_invitesService.GetInviteByEmail(email) != null)
				{
					ModelState.AddModelError("EmailAddresses", string.Format("{0} already has a pending or completed invite.", email));
				}
			}

			foreach (var email in emails)
			{
				if (!StringHelpers.ValidateEmail(email))
				{
					ModelState.AddModelError("EmailAddresses", string.Format("{0} does not appear to be valid. Check the address and try again.", email));
				}
			}

			foreach (var email in emails)
			{
				var user = _usersService.GetUserByEmail(email);
				if (user != null)
				{
					ModelState.AddModelError("EmailAddresses", string.Format("The email address {0} is already in use in this department on another. Email address can only be used once per account in the system.", email));
				}
			}

			if (ModelState.IsValid)
			{
				_invitesService.CreateInvites(model.Department, UserId, emails);

				return RedirectToAction("Invites", "Department", new { Area = "User" });
			}

			model.Invites = _invitesService.GetAllInvitesForDepartment(DepartmentId);

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult ResendInvite(int? inviteId)
		{
			if (inviteId.HasValue == false)
				return RedirectToAction("Invites", "Department", new { Area = "User" });

			if (_authorizationService.CanUserManageInvite(UserId, inviteId.Value))
				_invitesService.ResendInvite(inviteId.Value);

			return RedirectToAction("Invites", "Department", new { Area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteInvite(int? inviteId)
		{
			if (inviteId.HasValue == false)
				return RedirectToAction("Invites", "Department", new { Area = "User" });

			if (_authorizationService.CanUserManageInvite(UserId, inviteId.Value))
				_invitesService.DeleteInvite(inviteId.Value);

			return RedirectToAction("Invites", "Department", new { Area = "User" });
		}
		#endregion Invites

		#region Settings
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Settings()
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.SetUsers(_departmentsService.GetAllUsersForDepartment(DepartmentId, false, true), _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId));

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Users = new SelectList(model.Users, "Key", "Value");

			if (model.Department.Use24HourTime.HasValue)
				model.Use24HourTime = model.Department.Use24HourTime.Value;
			else
				model.Use24HourTime = false;

			var address = _departmentSettingsService.GetBigBoardCenterAddressDepartment(DepartmentId);
			var zoomLevel = _departmentSettingsService.GetBigBoardMapZoomLevelForDepartment(DepartmentId);
			var refreshTimer = _departmentSettingsService.GetBigBoardRefreshTimeForDepartment(DepartmentId);
			var mapCenterGpsCoordilates = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(DepartmentId);
			var mapHideUnavailable = _departmentSettingsService.GetBigBoardHideUnavailableDepartment(DepartmentId);
			var activeCallRssKey = _departmentSettingsService.GetRssKeyForDepartment(DepartmentId);
			model.DisableAutoAvailable = _departmentSettingsService.GetDisableAutoAvailableForDepartment(DepartmentId);

			var staffingLevels = _customStateService.GetActiveStaffingLevelsForDepartment(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			var actionLogs = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);
			if (actionLogs == null)
			{
				model.StatusLevels = model.UserStatusTypes.ToSelectListInt();
			}
			else
			{
				model.StatusLevels = new SelectList(actionLogs.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			if (zoomLevel.HasValue)
				model.MapZoomLevel = zoomLevel.Value.ToString();
			else
				model.MapZoomLevel = "10";

			if (refreshTimer.HasValue)
				model.RefreshTime = refreshTimer.Value.ToString();
			else
				model.RefreshTime = "30";

			if (mapHideUnavailable.HasValue)
				model.MapHideUnavailable = mapHideUnavailable.Value;

			if (!String.IsNullOrWhiteSpace(activeCallRssKey))
				model.ActiveCallRssKey = activeCallRssKey;

			if (!String.IsNullOrWhiteSpace(mapCenterGpsCoordilates))
			{
				string[] coordinates = mapCenterGpsCoordilates.Split(char.Parse(","));

				if (coordinates.Count() == 2)
				{
					model.MapCenterGpsCoordinatesLatitude = coordinates[0];
					model.MapCenterGpsCoordinatesLongitude = coordinates[1];
				}
			}

			if (model.Department.Address == null)
			{
				if (model.Department.AddressId.HasValue)
					model.Department.Address = _addressService.GetAddressById(model.Department.AddressId.Value);
				else
					model.Department.Address = new Address();
			}

			if (address != null)
			{
				model.MapCenterPointAddressAddress1 = address.Address1;
				model.MapCenterPointAddressCity = address.City;
				model.MapCenterPointAddressState = address.State;
				model.MapCenterPointAddressPostalCode = address.PostalCode;
				model.MapCenterPointAddressCountry = address.Country;
			}

			// Staffing Reset
			var tasks = _scheduledTasksService.GetScheduledTasksByUserType(model.Department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);
			if (tasks != null && tasks.Count > 0)
			{
				model.EnableStaffingReset = true;
				model.TimeToResetStaffing = tasks[0].Time;

				int result = 2; // Default to Unavailable
				int.TryParse(tasks[0].Data, out result);

				model.ResetStaffingTo = result;
			}

			// Status Reset
			var statusTask = _scheduledTasksService.GetScheduledTasksByUserType(model.Department.ManagingUserId, (int)TaskTypes.DepartmentStatusReset);
			if (statusTask != null && statusTask.Count > 0)
			{
				model.EnableStatusReset = true;
				model.TimeToResetStatus = statusTask[0].Time;

				int result = 0; // Default to Standing By
				int.TryParse(statusTask[0].Data, out result);

				model.ResetStatusTo = result;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Settings(DepartmentSettingsModel model)
		{
			Department d = _departmentsService.GetDepartmentEF(DepartmentId);
			d.TimeZone = model.Department.TimeZone;
			d.Name = model.Department.Name;
			d.ManagingUserId = model.Department.ManagingUserId;
			d.Use24HourTime = model.Use24HourTime;

			var staffingLevels = _customStateService.GetActiveStaffingLevelsForDepartment(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			var actionLogs = _customStateService.GetActivePersonnelStateForDepartment(DepartmentId);
			if (actionLogs == null)
			{
				model.StatusLevels = model.UserStatusTypes.ToSelectListInt();
			}
			else
			{
				model.StatusLevels = new SelectList(actionLogs.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			Address departmentAddress = null;
			if (model.Department.Address.AddressId != 0)
			{
				departmentAddress = _addressService.GetAddressById(model.Department.Address.AddressId);
				departmentAddress.Address1 = model.Department.Address.Address1;
				departmentAddress.City = model.Department.Address.City;
				departmentAddress.State = model.Department.Address.State;
				departmentAddress.PostalCode = model.Department.Address.PostalCode;
				departmentAddress.Country = model.Department.Address.Country;
			}
			else
				departmentAddress = model.Department.Address;

			model.User = _usersService.GetUserById(UserId);
			model.SetUsers(_departmentsService.GetAllUsersForDepartment(DepartmentId, false, true), _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId));

			if (!String.IsNullOrWhiteSpace(model.MapZoomLevel))
			{
				int mapZoomLevel;
				if (int.TryParse(model.MapZoomLevel, out mapZoomLevel))
				{
					if (mapZoomLevel < 0 || mapZoomLevel > 15)
						ModelState.AddModelError("MapZoomLevel", "Map zoom level must be between 0 and 15.");
				}
				else
				{
					ModelState.AddModelError("MapZoomLevel", "Map zoom level must be an integer number.");
				}
			}

			if (!String.IsNullOrWhiteSpace(model.RefreshTime))
			{
				int refreshTime;
				if (int.TryParse(model.RefreshTime, out refreshTime))
				{
					if (refreshTime < 5 || refreshTime > 120)
						ModelState.AddModelError("RefreshTime", "Map refresh time must be between 5 and 120.");
				}
				else
				{
					ModelState.AddModelError("RefreshTime", "Map refresh time must be between 5 and 120.");
				}
			}

			if (!String.IsNullOrWhiteSpace(model.MapCenterPointAddressAddress1))
			{
				if (String.IsNullOrWhiteSpace(model.MapCenterPointAddressCity) || String.IsNullOrWhiteSpace(model.MapCenterPointAddressCountry))
					ModelState.AddModelError("MapCenterPointAddressAddress1", "You need to specify a city and country if you have an address.");
			}

			if (model.EnableStaffingReset)
			{
				if (String.IsNullOrWhiteSpace(model.TimeToResetStaffing))
					ModelState.AddModelError("TimeToResetStaffing", "If you want to reset staffing levels you need to supply a time to reset them.");
			}

			if (model.EnableStatusReset)
			{
				if (String.IsNullOrWhiteSpace(model.TimeToResetStatus))
					ModelState.AddModelError("TimeToResetStatus", "If you want to reset status levels you need to supply a time to reset them.");
			}

			if (ModelState.IsValid)
			{
				departmentAddress = _addressService.SaveAddress(departmentAddress);
				d.AddressId = departmentAddress.AddressId;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.DepartmentSettingsChanged;
				auditEvent.Before = d.CloneJson();

				_departmentsService.UpdateDepartment(d);
				auditEvent.After = d;

				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
				model.Message = "Department settings save successful, you may have to log out and log back in for everything to take effect.";

				model.Department = d;

				if (!String.IsNullOrWhiteSpace(model.MapZoomLevel))
					_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.MapZoomLevel, DepartmentSettingTypes.BigBoardMapZoomLevel);

				if (!String.IsNullOrWhiteSpace(model.RefreshTime))
					_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.RefreshTime, DepartmentSettingTypes.BigBoardPageRefresh);

				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.MapHideUnavailable.ToString(), DepartmentSettingTypes.BigBoardHideUnavailable);
				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.DisableAutoAvailable.ToString(), DepartmentSettingTypes.DisabledAutoAvailable);

				if (!String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLatitude) && !String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLongitude))
					_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.MapCenterGpsCoordinatesLatitude + "," + model.MapCenterGpsCoordinatesLongitude, DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates);

				if (!String.IsNullOrWhiteSpace(model.MapCenterPointAddressAddress1))
				{
					var address = _departmentSettingsService.GetBigBoardCenterAddressDepartment(DepartmentId);

					if (address == null)
					{
						Address newAddress = new Address();
						newAddress.Address1 = model.MapCenterPointAddressAddress1;
						newAddress.City = model.MapCenterPointAddressCity;
						newAddress.State = model.MapCenterPointAddressState;
						newAddress.PostalCode = model.MapCenterPointAddressPostalCode;
						newAddress.Country = model.MapCenterPointAddressCountry;

						newAddress = _addressService.SaveAddress(newAddress);
						_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, newAddress.AddressId.ToString(), DepartmentSettingTypes.BigBoardMapCenterAddress);
					}
					else
					{
						address.Address1 = model.MapCenterPointAddressAddress1;
						address.City = model.MapCenterPointAddressCity;
						address.State = model.MapCenterPointAddressState;
						address.PostalCode = model.MapCenterPointAddressPostalCode;
						address.Country = model.MapCenterPointAddressCountry;

						_addressService.SaveAddress(address);
					}
				}

				if (model.EnableStaffingReset)
				{
					var task = new ScheduledTask();
					task.Active = true;
					task.TaskType = (int)TaskTypes.DepartmentStaffingReset;
					task.Data = model.ResetStaffingTo.ToString();
					task.AddedOn = DateTime.UtcNow;
					task.Time = model.TimeToResetStaffing;
					task.Monday = true;
					task.Tuesday = true;
					task.Wednesday = true;
					task.Thursday = true;
					task.Friday = true;
					task.Saturday = true;
					task.Sunday = true;
					task.UserId = model.Department.ManagingUserId;
					task.DepartmentId = model.Department.DepartmentId;

					_scheduledTasksService.DeleteDepartmentStaffingResetJob(model.Department.DepartmentId);
					_scheduledTasksService.SaveScheduledTask(task);
				}
				else
				{
					model.ResetStaffingTo = (int)UserStateTypes.Available;
					model.TimeToResetStaffing = String.Empty;
					_scheduledTasksService.DeleteDepartmentStaffingResetJob(model.Department.DepartmentId);
				}

				if (model.EnableStatusReset)
				{
					var task = new ScheduledTask();
					task.Active = true;
					task.TaskType = (int)TaskTypes.DepartmentStatusReset;
					task.Data = model.ResetStatusTo.ToString();
					task.AddedOn = DateTime.UtcNow;
					task.Time = model.TimeToResetStatus;
					task.Monday = true;
					task.Tuesday = true;
					task.Wednesday = true;
					task.Thursday = true;
					task.Friday = true;
					task.Saturday = true;
					task.Sunday = true;
					task.UserId = model.Department.ManagingUserId;
					task.DepartmentId = model.Department.DepartmentId;

					_scheduledTasksService.DeleteDepartmentStatusResetJob(model.Department.DepartmentId);
					_scheduledTasksService.SaveScheduledTask(task);
				}
				else
				{
					model.ResetStatusTo = (int)ActionTypes.StandingBy;
					model.TimeToResetStatus = String.Empty;
					_scheduledTasksService.DeleteDepartmentStatusResetJob(model.Department.DepartmentId);
				}

				_eventAggregator.SendMessage<DepartmentSettingsChangedEvent>(new DepartmentSettingsChangedEvent(){DepartmentId = DepartmentId});

				_departmentsService.InvalidateAllDepartmentsCache(DepartmentId);

				return RedirectToAction("Settings", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Api()
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			var activeCallRssKey = _departmentSettingsService.GetRssKeyForDepartment(DepartmentId);
			if (!String.IsNullOrWhiteSpace(activeCallRssKey))
				model.ActiveCallRssKey = activeCallRssKey;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult ProvisionApiKey()
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();

			Department d = _departmentsService.GetDepartmentById(DepartmentId);
			d.ApiKey = Guid.NewGuid().ToString("N");
			_departmentsService.UpdateDepartment(d);

			model.Department = d;

			return RedirectToAction("Api", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult ProvisionApiKeyAsync()
		{
			Department d = _departmentsService.GetDepartmentById(DepartmentId);
			d.ApiKey = Guid.NewGuid().ToString("N");
			_departmentsService.UpdateDepartment(d);

			return Content(d.ApiKey);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult ProvisionActiveCallRssKey()
		{
			_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, Guid.NewGuid().ToString("N"), DepartmentSettingTypes.RssFeedKeyForActiveCalls);

			return RedirectToAction("Api", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Address()
		{
			SettingsAddressModel model = new SettingsAddressModel();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			if (model.Department.Address == null)
			{
				if (model.Department.AddressId.HasValue)
					model.Department.Address = _addressService.GetAddressById(model.Department.AddressId.Value);
				else
					model.Department.Address = new Address();
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Address(SettingsAddressModel model)
		{
			Address address = null;

			if (model.Department.Address.AddressId != 0)
			{
				address = _addressService.GetAddressById(model.Department.Address.AddressId);
				address.AddressId = model.Department.Address.AddressId;
				address.Address1 = model.Department.Address.Address1;
				address.City = model.Department.Address.City;
				address.State = model.Department.Address.State;
				address.PostalCode = model.Department.Address.PostalCode;
				address.Country = model.Department.Address.Country;
			}
			else
				address = model.Department.Address;

			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(UserId);


			if (ModelState.IsValid)
			{
				//using (var scope = new TransactionScope())
				//{
				address = _addressService.SaveAddress(address);

				model.Department.AddressId = address.AddressId;
				_departmentsService.UpdateDepartment(model.Department);

				//	scope.Complete();
				//}
				model.Message = "Department address settings save successful";

				return View(model);
			}

			model.ErrorMessage = "Unable to save the address.";

			return View(model);
		}
		#endregion Settings

		#region User States
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SetUserResponding(string userId)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
																			 (int)ActionTypes.Responding);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SetUserNotResponding(string userId)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
																			 (int)ActionTypes.NotResponding);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SetUserStandingBy(string userId)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
																			 (int)ActionTypes.StandingBy);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SetUserOnScene(string userId)
		{
			_actionLogsService.SetUserAction(userId, _departmentsService.GetDepartmentByUserId(UserId).DepartmentId,
																			 (int)ActionTypes.OnScene);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}
		#endregion User States

		#region Call Settings
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult CallSettings()
		{
			var model = new CallSettingsView();
			model.EmailSettings = _departmentsService.GetDepartmentEmailSettings(DepartmentId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);

			var internalDispatchEmail = _departmentSettingsService.GetDispatchEmailForDepartment(DepartmentId);
			if (!String.IsNullOrWhiteSpace(internalDispatchEmail))
				model.InternalDispatchEmail = $"{internalDispatchEmail}@{Config.InboundEmailConfig.DispatchDomain}";

			if (model.EmailSettings == null)
			{
				model.EmailSettings = new DepartmentCallEmail();
				model.EmailSettings.Port = 110;
			}
			else
			{
				model.CallType = model.EmailSettings.FormatType;
			}

			if (model.CallType == 0)
				model.CallType = (int)CallEmailTypes.Generic;

			var callPruning = _departmentsService.GetDepartmentCallPruningSettings(DepartmentId);

			if (callPruning != null)
			{
				if (callPruning.PruneEmailImportedCalls.HasValue)
					model.PruneEmailCalls = callPruning.PruneEmailImportedCalls.Value;

				if (callPruning.EmailImportCallPruneInterval.HasValue)
					model.MinutesTillPrune = callPruning.EmailImportCallPruneInterval.Value;
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult CallSettings(CallSettingsView model)
		{
			model.CallTypes = _callsService.GetCallTypesForDepartment(DepartmentId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var internalDispatchEmail = _departmentSettingsService.GetDispatchEmailForDepartment(DepartmentId);
			if (!String.IsNullOrWhiteSpace(internalDispatchEmail))
				model.InternalDispatchEmail = $"{internalDispatchEmail}@{Config.InboundEmailConfig.DispatchDomain}";

			//if (model.CallType <= 0)
			//	ModelState.AddModelError("", "You need to select a email type from the list. If you don't see your email type, contact us to get it added.");

			//if (String.IsNullOrWhiteSpace(model.EmailSettings.Hostname) && !String.IsNullOrWhiteSpace(_emailService.TestEmailSettings(model.EmailSettings)))
			//	ModelState.AddModelError("", "Call Email Settings appear incorrect. Check your email settings and try again.");

			if (ModelState.IsValid)
			{
				DepartmentCallEmail emailSettings = _departmentsService.GetDepartmentEmailSettings(DepartmentId);
				model.EmailSettings.DepartmentId = DepartmentId;
				model.EmailSettings.FormatType = model.CallType;

				if (emailSettings == null)
					emailSettings = new DepartmentCallEmail();

				emailSettings.DepartmentId = DepartmentId;
				emailSettings.Hostname = model.EmailSettings.Hostname;
				emailSettings.Password = model.EmailSettings.Password;
				emailSettings.Port = model.EmailSettings.Port;
				emailSettings.Username = model.EmailSettings.Username;
				emailSettings.FormatType = model.EmailSettings.FormatType;
				emailSettings.UseSsl = model.EmailSettings.UseSsl;

				model.EmailSettings = _departmentsService.SaveDepartmentEmailSettings(emailSettings);

				DepartmentCallPruning pruningSettings = _departmentsService.GetDepartmentCallPruningSettings(DepartmentId);

				if (pruningSettings == null)
					pruningSettings = new DepartmentCallPruning();

				pruningSettings.DepartmentId = DepartmentId;
				pruningSettings.PruneEmailImportedCalls = model.PruneEmailCalls;
				pruningSettings.EmailImportCallPruneInterval = model.MinutesTillPrune;

				pruningSettings = _departmentsService.SavelDepartmentCallPruning(pruningSettings);

				if (pruningSettings.PruneEmailImportedCalls.HasValue)
					model.PruneEmailCalls = pruningSettings.PruneEmailImportedCalls.Value;

				if (pruningSettings.EmailImportCallPruneInterval.HasValue)
					model.MinutesTillPrune = pruningSettings.EmailImportCallPruneInterval.Value;

				_departmentsService.InvalidateAllDepartmentsCache(DepartmentId);

				model.Message = "Email settings were successfully saved.";
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult GetCallEmailTypes()
		{
			List<CallEmailTypesForJson> callEmailTypes = new List<CallEmailTypesForJson>();
			var values = Enum.GetValues(typeof(CallEmailTypes)).Cast<CallEmailTypes>();

			foreach (var v in values)
			{
				switch (v)
				{
					case CallEmailTypes.CalFireECC:
						CallEmailTypesForJson calFireEcc = new CallEmailTypesForJson();
						calFireEcc.Id = (int)v;
						calFireEcc.Name = "CAL FIRE ECC";
						calFireEcc.Code = v.ToString();
						callEmailTypes.Add(calFireEcc);
						break;
					case CallEmailTypes.CarencroFire:
						CallEmailTypesForJson carencroFire = new CallEmailTypesForJson();
						carencroFire.Id = (int)v;
						carencroFire.Name = "Carencro";
						carencroFire.Code = v.ToString();
						callEmailTypes.Add(carencroFire);
						break;
					case CallEmailTypes.Resgrid:
						CallEmailTypesForJson resgrid = new CallEmailTypesForJson();
						resgrid.Id = (int)v;
						resgrid.Name = "Resgrid";
						resgrid.Code = v.ToString();
						callEmailTypes.Add(resgrid);
						break;
					case CallEmailTypes.GrandBlanc:
						CallEmailTypesForJson grandBlanc = new CallEmailTypesForJson();
						grandBlanc.Id = (int)v;
						grandBlanc.Name = "Grand Blanc";
						grandBlanc.Code = v.ToString();
						callEmailTypes.Add(grandBlanc);
						break;
					case CallEmailTypes.Generic:
						CallEmailTypesForJson generic = new CallEmailTypesForJson();
						generic.Id = (int)v;
						generic.Name = "Generic";
						generic.Code = v.ToString();
						callEmailTypes.Add(generic);
						break;
					case CallEmailTypes.LowestoftCoastGuard:
						CallEmailTypesForJson lowestoftCoastGuard = new CallEmailTypesForJson();
						lowestoftCoastGuard.Id = (int)v;
						lowestoftCoastGuard.Name = "Lowestoft";
						lowestoftCoastGuard.Code = v.ToString();
						callEmailTypes.Add(lowestoftCoastGuard);
						break;
					case CallEmailTypes.UnionFire:
						CallEmailTypesForJson unionFire = new CallEmailTypesForJson();
						unionFire.Id = (int)v;
						unionFire.Name = "Union Fire";
						unionFire.Code = v.ToString();
						callEmailTypes.Add(unionFire);
						break;
					case CallEmailTypes.ParklandCounty:
						CallEmailTypesForJson parklandCounty = new CallEmailTypesForJson();
						parklandCounty.Id = (int)v;
						parklandCounty.Name = "Parkland Cty";
						parklandCounty.Code = v.ToString();
						callEmailTypes.Add(parklandCounty);
						break;
					case CallEmailTypes.GenericPage:
						CallEmailTypesForJson genericPage = new CallEmailTypesForJson();
						genericPage.Id = (int)v;
						genericPage.Name = "Generic Page";
						genericPage.Code = v.ToString();
						callEmailTypes.Add(genericPage);
						break;
					case CallEmailTypes.Brockport:
						CallEmailTypesForJson brockport = new CallEmailTypesForJson();
						brockport.Id = (int)v;
						brockport.Name = "Brockport";
						brockport.Code = v.ToString();
						callEmailTypes.Add(brockport);
						break;
					case CallEmailTypes.HancockCounty:
						CallEmailTypesForJson hancock = new CallEmailTypesForJson();
						hancock.Id = (int)v;
						hancock.Name = "Hancock Cty";
						hancock.Code = v.ToString();
						callEmailTypes.Add(hancock);
						break;
					case CallEmailTypes.CalFireSCU:
						CallEmailTypesForJson calFireSCU = new CallEmailTypesForJson();
						calFireSCU.Id = (int)v;
						calFireSCU.Name = "CAL FIRE SCU";
						calFireSCU.Code = v.ToString();
						callEmailTypes.Add(calFireSCU);
						break;
					case CallEmailTypes.Connect:
						CallEmailTypesForJson connect = new CallEmailTypesForJson();
						connect.Id = (int)v;
						connect.Name = "Connect";
						connect.Code = v.ToString();
						callEmailTypes.Add(connect);
						break;
					case CallEmailTypes.SpottedDog:
						CallEmailTypesForJson spottedDog = new CallEmailTypesForJson();
						spottedDog.Id = (int)v;
						spottedDog.Name = "Spotted Dog";
						spottedDog.Code = v.ToString();
						callEmailTypes.Add(spottedDog);
						break;
					case CallEmailTypes.PortJervis:
						CallEmailTypesForJson portJervis = new CallEmailTypesForJson();
						portJervis.Id = (int)v;
						portJervis.Name = "PortJervis";
						portJervis.Code = v.ToString();
						callEmailTypes.Add(portJervis);
						break;
					case CallEmailTypes.Yellowhead:
						CallEmailTypesForJson yellowhead = new CallEmailTypesForJson();
						yellowhead.Id = (int)v;
						yellowhead.Name = "Yellowhead";
						yellowhead.Code = v.ToString();
						callEmailTypes.Add(yellowhead);
						break;
					case CallEmailTypes.ParklandCounty2:
						CallEmailTypesForJson parklandCounty2 = new CallEmailTypesForJson();
						parklandCounty2.Id = (int)v;
						parklandCounty2.Name = "Parkland Cty2";
						parklandCounty2.Code = v.ToString();
						callEmailTypes.Add(parklandCounty2);
						break;
				}
			}

			return Json(callEmailTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult ClearDepartmentCache()
		{
			CqrsEvent clearDepartmentCacheEvent = new CqrsEvent();
			clearDepartmentCacheEvent.Type = (int)CqrsEventTypes.ClearDepartmentCache;
			clearDepartmentCacheEvent.Data = DepartmentId.ToString();

			_cqrsProvider.EnqueueCqrsEvent(clearDepartmentCacheEvent);

			return Json("true");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteCallEmailSettings()
		{
			_departmentsService.DeleteDepartmentEmailSettings(DepartmentId);

			return RedirectToAction("CallSettings");
		}
		#endregion Call Settings

		#region Unit Settings
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult UnitSettings()
		{
			UnitSettingsView model = new UnitSettingsView();
			model.UnitTypes = _unitsService.GetUnitTypesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteUnitType(int unitTypeId)
		{
			_unitsService.DeleteUnitType(unitTypeId);

			return RedirectToAction("Types");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult NewUnitType(UnitSettingsView model)
		{
			if (String.IsNullOrWhiteSpace(model.NewUnitType))
				ModelState.AddModelError("NewUnitType", "You Must specify the new unit type.");

			if (ModelState.IsValid)
			{
				_unitsService.AddUnitType(DepartmentId, model.NewUnitType, model.UnitCustomStatesId);
			}

			return RedirectToAction("Types");
		}
		#endregion Unit Settings

		#region Types
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult Types()
		{
			var model = new DepartmentTypesView();
			model.CertificationTypes = _certificationService.GetAllCertificationTypesByDepartment(DepartmentId);
			model.UnitTypes = _unitsService.GetUnitTypesForDepartment(DepartmentId);
			model.CallTypes = _callsService.GetCallTypesForDepartment(DepartmentId);
			
			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(_customStateService.GetAllActiveUnitStatesForDepartment(DepartmentId));
			model.States = states;

			model.CallPriorites = _callsService.GetActiveCallPrioritesForDepartment(DepartmentId);

			if (model.CallPriorites == null || !model.CallPriorites.Any())
			{
				model.CallPriorites = _callsService.GetDefaultCallPriorites();

				//Sounds don't need to come back to the UI
				foreach(var priority in model.CallPriorites)
				{
					priority.PushNotificationSound = null;
					priority.ShortNotificationSound = null;
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteCertificationType(int certificationTypeId)
		{
			_certificationService.DeleteCertificationTypeById(certificationTypeId);

			return RedirectToAction("Types");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult NewCertificationType(DepartmentTypesView model)
		{
			if (String.IsNullOrEmpty(model.NewCertificationType))
				ModelState.AddModelError("NewCertificationType", "You Must specify the new certification type.");

			if (ModelState.IsValid)
			{
				_certificationService.SaveNewCertificationType(model.NewCertificationType, DepartmentId);
			}

			return RedirectToAction("Types");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult DeleteCallType(int callTypeId)
		{
			_callsService.DeleteCallType(callTypeId);

			return RedirectToAction("Types");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult NewCallType(CallSettingsView model)
		{
			if (String.IsNullOrEmpty(model.NewCallType))
				ModelState.AddModelError("NewCallType", "You Must specify the new call type.");

			if (ModelState.IsValid)
			{
				_callsService.SaveNewCallType(model.NewCallType, DepartmentId);
			}

			return RedirectToAction("Types");
		}
		#endregion Types

		#region Text Messaging
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult TextSettings()
		{
			var model = new TextSetupModel();

			model.CanProvisionNumber = _limitsService.CanDepartmentProvisionNumber(DepartmentId);
			model.DepartmentTextToCallNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			model.DepartmentTextToCallSourceNumbers = _departmentSettingsService.GetTextToCallSourceNumbersForDepartment(DepartmentId);
			model.EnableTextToCall = _departmentSettingsService.GetDepartmentIsTextCallImportEnabled(DepartmentId);
			model.EnableTextCommand = _departmentSettingsService.GetDepartmentIsTextCommandEnabled(DepartmentId);

			var textImportFormat = _departmentSettingsService.GetTextToCallImportFormatForDepartment(DepartmentId);
			if (textImportFormat.HasValue)
				model.TextCallType = textImportFormat.Value;

			return View(model);
		}


		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult TextSettings(TextSetupModel model)
		{
			model.CanProvisionNumber = _limitsService.CanDepartmentProvisionNumber(DepartmentId);
			model.DepartmentTextToCallNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);

			var textImportFormat = _departmentSettingsService.GetTextToCallImportFormatForDepartment(DepartmentId);
			if (textImportFormat.HasValue)
				model.TextCallType = textImportFormat.Value;

			if (model.EnableTextCommand && model.EnableTextToCall && String.IsNullOrWhiteSpace(model.DepartmentTextToCallSourceNumbers))
				ModelState.AddModelError("DepartmentTextToCallSourceNumbers", "You have enabled Text-To-Call and Text Command, to prevent all personel command texts being imported as calls you must supply the phone numbers that text dispatches will orginiate from.");

			if (ModelState.IsValid)
			{
				if (!String.IsNullOrWhiteSpace(model.DepartmentTextToCallSourceNumbers))
					_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.DepartmentTextToCallSourceNumbers, DepartmentSettingTypes.TextToCallSourceNumbers);
				else
					_departmentSettingsService.DeleteSetting(DepartmentId, DepartmentSettingTypes.TextToCallSourceNumbers);

				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.TextCallType.ToString(), DepartmentSettingTypes.TextToCallImportFormat);
				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.EnableTextToCall.ToString(), DepartmentSettingTypes.EnableTextToCall);
				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, model.EnableTextCommand.ToString(), DepartmentSettingTypes.EnableTextCommand);
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult GetAvailableNumbers(string country, string areaCode)
		{
			return Json(_numbersService.GetAvailableNumbers(country, areaCode));
		}

		[HttpGet]
		public IActionResult ProvisionNumber(string msisdn, string country)
		{
			if (!_limitsService.CanDepartmentProvisionNumber(DepartmentId))
				return RedirectToAction("Unauthorized", "Public", new { Area = "" });

			var success = _numbersService.ProvisionNumber(DepartmentId, msisdn, country);

			if (success)
			{
				_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, "0", DepartmentSettingTypes.TextToCallImportFormat);
				return RedirectToAction("TextSettings");
			}

			return RedirectToAction("ProvisionFailed");
		}

		[HttpGet]
		public IActionResult ProvisionDefaultNumberAsync(string country, string areaCode)
		{
			if (!_limitsService.CanDepartmentProvisionNumber(DepartmentId))
				return RedirectToAction("Unauthorized", "Public", new { Area = "" });

			var numbers = _numbersService.GetAvailableNumbers(country, areaCode);

			if (numbers.Count > 0)
			{
				var success = _numbersService.ProvisionNumber(DepartmentId, numbers.First().msisdn, country);

				if (success)
				{
					_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, "0", DepartmentSettingTypes.TextToCallImportFormat);

					return Content(numbers.First().msisdn);
				}
			}

			return Content("Could not provision number");
		}

		public IActionResult ProvisionFailed()
		{
			return View();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult GetCallTextTypes()
		{
			var callEmailTypes = new List<CallEmailTypesForJson>();
			var values = Enum.GetValues(typeof(CallTextTypes)).Cast<CallTextTypes>();

			foreach (var v in values)
			{
				switch (v)
				{
					case CallTextTypes.Generic:
						var generic = new CallEmailTypesForJson();
						generic.Id = (int)v;
						generic.Name = "Generic";
						generic.Code = v.ToString();
						callEmailTypes.Add(generic);
						break;
				}
			}

			return Json(callEmailTypes);
		}
		#endregion Text Messaging

		#region Stations Grid
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult GetStationsForGrid()
		{
			var stations = new List<StationJson>();

			var groups = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

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
		[Authorize(Policy = ResgridResources.Department_View)]
		public PartialViewResult SmallStationGroupsGrid()
		{
			return PartialView("_SmallStationGroupsGridPartial");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult GetDepartmentTypes()
		{
			var model = new RegisterViewModel();

			return Json(new SelectList(model.DepartmentTypes));
		}
		#endregion Stations Grid

		#region Recipients
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult GetRecipientsForGrid(int filter = 0, bool filterSelf = false, bool filterNotInGroup = false, int ignoreGroupId = 0)
		{
			var result = new List<RecipientJson>();

			var stations = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			var roles = _personnelRolesService.GetRolesForDepartment(DepartmentId);
			var profiles = _userProfileService.GetAllProfilesForDepartment(DepartmentId);

			if (filter == 0 || filter == 1)
			{
				foreach (var s in stations)
				{
					var respondingTo = new RecipientJson();
					respondingTo.Id = s.DepartmentGroupId.ToString();
					respondingTo.Type = "Group";
					respondingTo.Name = s.Name;

					result.Add(respondingTo);
				}
			}

			if (filter == 0 || filter == 2)
			{
				foreach (var r in roles)
				{
					var respondingTo = new RecipientJson();
					respondingTo.Id = r.PersonnelRoleId.ToString();
					respondingTo.Type = "Role";
					respondingTo.Name = r.Name;

					result.Add(respondingTo);
				}
			}

			if (filter == 0 || filter == 3)
			{
				var groupUsers = stations.Where(x => x.DepartmentGroupId != ignoreGroupId).SelectMany(x => x.Members);

				foreach (var p in profiles)
				{
					if (p.Value != null)
					{
						var respondingTo = new RecipientJson();
						respondingTo.Id = p.Value.UserId.ToString();
						respondingTo.Type = "Person";
						respondingTo.Name = p.Value.FullName.AsFirstNameLastName;

						if ((filterSelf && p.Value.UserId != UserId) ||
							(filterNotInGroup && !groupUsers.Any(x => x.UserId == p.Value.UserId)) ||
							(!filterSelf && !filterNotInGroup))
							result.Add(respondingTo);
						//else
						//	result.Add(respondingTo);
					}
				}
			}

			return Json(result);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public PartialViewResult RecipientsGrid()
		{
			return PartialView("_RecipientsGridPartial");
		}
		#endregion Recipients

		#region Aync Site Parts
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult GetSubscriptionLimitWarning()
		{
			if (!_limitsService.ValidateDepartmentIsWithinLimits(DepartmentId))
				return Content("Invalid");

			return new EmptyResult();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public PartialViewResult TopIconsArea()
		{
			TopIconsModel model = new TopIconsModel();
			model.SetMessages(UserId, DepartmentId);

			return PartialView("_TopIconsPartial", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult UpgradeButton()
		{
			if (!Config.SystemBehaviorConfig.RedirectHomeToLogin)
			{
				var plan = _subscriptionsService.GetCurrentPlanForDepartment(DepartmentId);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && plan != null && plan.PlanId == 1)
					return PartialView("_TopUpgradePartial");
			}

			return Content("<span></span>");
		}
		#endregion Aync Site Parts

		#region Async Validation Methods
		//[HttpPost]
		//[Authorize(Policy = ResgridResources.Department_View)]
		//public IActionResult TestEmailSettings(EmailTestInput input)
		//{
		//	string returnText = "";

		//	if (ModelState.IsValid)
		//	{
		//		var email = new DepartmentCallEmail();
		//		email.Hostname = input.Hostname;
		//		email.Port = input.Port;
		//		email.UseSsl = input.Encryption;
		//		email.Username = input.Username;
		//		email.Password = input.Password;

		//		returnText = _emailService.TestEmailSettings(email);
		//	}
		//	else
		//	{
		//		returnText = "Please supply a hostname, username and password.";
		//	}

		//	return Content(returnText);
		//}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult ValidateAddress(Address address)
		{
			string returnText = "";

			//SJ: Disabling for now.
			var result = _addressService.IsAddressValid(address);

			if (result.ServiceSuccess && !result.AddressValid)
				returnText = "Address looks like it might be invalid. Verify it before you continue.";

			return Content(returnText);
		}
		#endregion Async Validation Methods

		#region Setup Wizard
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public IActionResult SetupWizard()
		{
			var model = new SetupWizardView();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);
			model.Units = _unitsService.GetUnitsForDepartment(DepartmentId);
			model.CanProvisionNumber = _limitsService.CanDepartmentProvisionNumber(DepartmentId);
			model.DepartmentTextToCallNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			model.DepartmentTextToCallSourceNumbers = _departmentSettingsService.GetTextToCallSourceNumbersForDepartment(DepartmentId);
			model.EmailSettings = _departmentsService.GetDepartmentEmailSettings(DepartmentId);
			model.DepartmentEmailAddress = _departmentSettingsService.GetDispatchEmailForDepartment(DepartmentId);

			return View("_SetupWizard", model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public IActionResult SubmitSetupWizard([FromBody]SetupWizardFormPayload payload)
		{
			var formCollection = JsonConvert.DeserializeObject<Dictionary<string, string>>(payload.setupWizardForm);

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			department.TimeZone = formCollection["Department.TimeZone"];

			_departmentSettingsService.SaveOrUpdateSetting(DepartmentId, formCollection["DisableAutoAvailable"], DepartmentSettingTypes.DisabledAutoAvailable);

			Address address = null;
			if (department.AddressId.HasValue)
				address = _addressService.GetAddressById(department.AddressId.Value);
			else
				address = new Address();

			address.Address1 = formCollection["Department.Address.Address1"];
			address.City = formCollection["Department.Address.City"];
			address.State = formCollection["Department.Address.State"];
			address.PostalCode = formCollection["Department.Address.PostalCode"];
			address.Country = formCollection["Department.Address.Country"];

			address = _addressService.SaveAddress(address);
			department.AddressId = address.AddressId;

			_departmentsService.SaveDepartment(department);

			int stationCount = int.Parse(formCollection["StationCount"]);
			var newStations = new List<DepartmentGroup>();
			for (int i = 1; i <= stationCount; i++)
			{
				var station = new DepartmentGroup();
				var stationAddress = new Address();

				station.Name = formCollection["station_" + i];
				station.Type = (int)DepartmentGroupTypes.Station;
				station.DepartmentId = DepartmentId;
				stationAddress.Address1 = formCollection["stationAddress1_" + i];
				stationAddress.City = formCollection["stationCity_" + i];
				stationAddress.State = formCollection["stationState_" + i];
				stationAddress.PostalCode = formCollection["stationPostalCode_" + i];
				stationAddress.Country = formCollection["stationCountry_" + i];

				stationAddress = _addressService.SaveAddress(stationAddress);
				station.AddressId = stationAddress.AddressId;

				newStations.Add(_departmentGroupsService.Save(station));
			}

			int unitsCount = int.Parse(formCollection["UnitCount"]);
			var newUnits = new List<Unit>();
			for (int i = 1; i <= unitsCount; i++)
			{
				var unit = new Unit();
				unit.Name = formCollection["unit_" + i];
				unit.DepartmentId = DepartmentId;

				newUnits.Add(_unitsService.SaveUnit(unit));
			}

			if (formCollection["CallImportOption"] == "Email Call Importing")
			{
				DepartmentCallEmail emailSettings = _departmentsService.GetDepartmentEmailSettings(DepartmentId);
				if (emailSettings == null)
					emailSettings = new DepartmentCallEmail();

				emailSettings.DepartmentId = DepartmentId;
				emailSettings.Hostname = formCollection["EmailSettings.Hostname"];
				emailSettings.Password = formCollection["EmailSettings.Password"];
				emailSettings.Port = int.Parse(formCollection["EmailSettings.Port"]);
				emailSettings.Username = formCollection["EmailSettings.Username"];
				emailSettings.FormatType = (int)CallEmailTypes.Generic;
				emailSettings.UseSsl = bool.Parse(formCollection["EmailSettings.UseSsl"]);

				_departmentsService.SaveDepartmentEmailSettings(emailSettings);
			}


			return new JsonResult("{}");
		}
		#endregion Setup Wizard
	}

	public class SetupWizardFormPayload
	{
		public string setupWizardForm { get; set; }
	}
}
