using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
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
using AuditEvent = Resgrid.Model.Events.AuditEvent;
using Microsoft.AspNetCore.Http;

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
		private readonly IPrinterProvider _printerProvider;
		private readonly IQueueService _queueService;
		private readonly IDocumentsService _documentsService;
		private readonly INotesService _notesService;
		private readonly IContactsService _contactsService;

		public DepartmentController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IDepartmentGroupsService departmentGroupsService, IUserProfileService userProfileService, IDeleteService deleteService,
			IInvitesService invitesService, Model.Services.IAuthorizationService authorizationService, IAddressService addressService, ISubscriptionsService subscriptionsService,
			ILimitsService limitsService, ICallsService callsService, IDepartmentSettingsService departmentSettingsService, IUnitsService unitsService,
			ICertificationService certificationService, INumbersService numbersService, IScheduledTasksService scheduledTasksService, IPersonnelRolesService personnelRolesService,
			IEventAggregator eventAggregator, ICustomStateService customStateService, ICqrsProvider cqrsProvider, IPrinterProvider printerProvider, IQueueService queueService,
			IDocumentsService documentsService, INotesService notesService, IContactsService contactsService)
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
			_printerProvider = printerProvider;
			_queueService = queueService;
			_documentsService = documentsService;
			_notesService = notesService;
			_contactsService = contactsService;
		}

		#endregion Private Members and Constructors

		#region Invites

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Invites()
		{
			var model = new InvitesView();
			model.Invites = await _invitesService.GetAllInvitesForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (model.Invites == null)
				model.Invites = new List<Invite>();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Invites(InvitesView model, CancellationToken cancellationToken)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			List<string> emails = new List<string>();
			string[] rawEmails = model.EmailAddresses.Split(char.Parse(","));

			foreach (var s in rawEmails)
			{
				if (!String.IsNullOrEmpty(s.Trim()))
					emails.Add(s.Trim());
			}

			foreach (var email in emails)
			{
				if (await _invitesService.GetInviteByEmailAsync(email) != null)
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
					ModelState.AddModelError("EmailAddresses",
						string.Format(
							"The email address {0} is already in use in this department or another. Email address can only be used once per account in the system. If the user previously has a Resgrid account they need to be added via the Add a Person page.",
							email));
				}
			}

			if (ModelState.IsValid)
			{
				await _invitesService.CreateInvitesAsync(model.Department, UserId, emails, cancellationToken);

				return RedirectToAction("Invites", "Department", new { Area = "User" });
			}

			model.Invites = await _invitesService.GetAllInvitesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ResendInvite(int? inviteId, CancellationToken cancellationToken)
		{
			if (inviteId.HasValue == false)
				return RedirectToAction("Invites", "Department", new { Area = "User" });

			if (await _authorizationService.CanUserManageInviteAsync(UserId, inviteId.Value))
				await _invitesService.ResendInviteAsync(inviteId.Value, cancellationToken);

			return RedirectToAction("Invites", "Department", new { Area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteInvite(int? inviteId, CancellationToken cancellationToken)
		{
			if (inviteId.HasValue == false)
				return RedirectToAction("Invites", "Department", new { Area = "User" });

			if (await _authorizationService.CanUserManageInviteAsync(UserId, inviteId.Value))
				await _invitesService.DeleteInviteAsync(inviteId.Value, cancellationToken);

			return RedirectToAction("Invites", "Department", new { Area = "User" });
		}

		#endregion Invites

		#region Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Settings()
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.SetUsers(await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, false, true),
				await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId));

			// Default to PST so there is at least something. Sorry everyone else.
			if (String.IsNullOrWhiteSpace(model.Department.TimeZone))
				model.Department.TimeZone = "Pacific Standard Time";

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Users = new SelectList(model.Users, "Key", "Value");

			PersonnelSortOrders personnelSortOrders = PersonnelSortOrders.Default;
			model.PersonnelSortTypes = personnelSortOrders.ToSelectListInt();

			UnitSortOrders unitSortTypes = UnitSortOrders.Default;
			model.UnitSortTypes = unitSortTypes.ToSelectListInt();

			CallSortOrders callSortTypes = CallSortOrders.Default;
			model.CallSortTypes = callSortTypes.ToSelectListInt();

			if (model.Department.Use24HourTime.HasValue)
				model.Use24HourTime = model.Department.Use24HourTime.Value;
			else
				model.Use24HourTime = false;

			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var zoomLevel = await _departmentSettingsService.GetBigBoardMapZoomLevelForDepartmentAsync(DepartmentId);
			var refreshTimer = await _departmentSettingsService.GetBigBoardRefreshTimeForDepartmentAsync(DepartmentId);
			var mapCenterGpsCoordilates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);
			var mapHideUnavailable = await _departmentSettingsService.GetBigBoardHideUnavailableDepartmentAsync(DepartmentId);
			var activeCallRssKey = await _departmentSettingsService.GetRssKeyForDepartmentAsync(DepartmentId);
			model.DisableAutoAvailable = await _departmentSettingsService.GetDisableAutoAvailableForDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			model.PersonnelSort = (int)personnelSortOrder;

			var callSortOrder = await _departmentSettingsService.GetDepartmentCallSortOrderAsync(DepartmentId);
			model.CallsSort = (int)callSortOrder;

			var unitsSortOrder = await _departmentSettingsService.GetDepartmentUnitsSortOrderAsync(DepartmentId);
			model.UnitsSort = (int)unitsSortOrder;

			var staffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.Staffings = _customStateService.GetDefaultPersonStaffings();
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			}
			else
			{
				model.Staffings = staffingLevels.GetActiveDetails();
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			model.SuppressStaffingInfo = await _departmentSettingsService.GetDepartmentStaffingSuppressInfoAsync(DepartmentId, false);

			if (model.SuppressStaffingInfo != null)
			{
				model.EnableStaffingSupress = model.SuppressStaffingInfo.EnableSupressStaffing;
			}

			var actionLogs = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
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
					model.MapCenterGpsCoordinatesLatitude = StringHelpers.SanitizeCoordinatesString(coordinates[0]);
					model.MapCenterGpsCoordinatesLongitude = StringHelpers.SanitizeCoordinatesString(coordinates[1]);
				}
			}

			if (model.Department.Address == null)
			{
				if (model.Department.AddressId.HasValue)
					model.Department.Address = await _addressService.GetAddressByIdAsync(model.Department.AddressId.Value);
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
			var tasks = await _scheduledTasksService.GetScheduledTasksByUserTypeAsync(model.Department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);
			if (tasks != null && tasks.Count > 0)
			{
				model.EnableStaffingReset = true;
				model.TimeToResetStaffing = tasks[0].Time;

				int result = 2; // Default to Unavailable
				int.TryParse(tasks[0].Data, out result);

				model.ResetStaffingTo = result;
			}
			else
			{
				model.EnableStaffingReset = false;
			}

			// Status Reset
			var statusTask = await _scheduledTasksService.GetScheduledTasksByUserTypeAsync(model.Department.ManagingUserId, (int)TaskTypes.DepartmentStatusReset);
			if (statusTask != null && statusTask.Count > 0)
			{
				model.EnableStatusReset = true;
				model.TimeToResetStatus = statusTask[0].Time;

				int result = 0; // Default to Standing By
				int.TryParse(statusTask[0].Data, out result);

				model.ResetStatusTo = result;
			}
			else
			{
				model.EnableStatusReset = false;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Settings(DepartmentSettingsModel model, IFormCollection form, CancellationToken cancellationToken)
		{
			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.DepartmentSettingsChanged;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

			Department d = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			auditEvent.Before = d.CloneJsonToString();

			d.TimeZone = model.Department.TimeZone;
			d.Name = model.Department.Name;
			d.ManagingUserId = model.Department.ManagingUserId;
			d.Use24HourTime = model.Use24HourTime;

			ViewBag.Countries = new SelectList(Countries.CountryNames);
			ViewBag.TimeZones = new SelectList(TimeZones.Zones, "Key", "Value");
			ViewBag.Users = new SelectList(model.Users, "Key", "Value");

			PersonnelSortOrders personnelSortOrders = PersonnelSortOrders.Default;
			model.PersonnelSortTypes = personnelSortOrders.ToSelectListInt();

			UnitSortOrders unitSortTypes = UnitSortOrders.Default;
			model.UnitSortTypes = unitSortTypes.ToSelectListInt();

			CallSortOrders callSortTypes = CallSortOrders.Default;
			model.CallSortTypes = callSortTypes.ToSelectListInt();

			var staffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.Staffings = _customStateService.GetDefaultPersonStaffings();
				model.StaffingLevels = model.UserStateTypes.ToSelectListInt();
			}
			else
			{
				model.Staffings = staffingLevels.GetActiveDetails();
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			model.SuppressStaffingInfo = await _departmentSettingsService.GetDepartmentStaffingSuppressInfoAsync(DepartmentId, false);

			if (model.SuppressStaffingInfo != null)
			{
				model.EnableStaffingSupress = model.SuppressStaffingInfo.EnableSupressStaffing;
			}

			var actionLogs = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
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
				departmentAddress = await _addressService.GetAddressByIdAsync(model.Department.Address.AddressId);
				departmentAddress.Address1 = model.Department.Address.Address1;
				departmentAddress.City = model.Department.Address.City;
				departmentAddress.State = model.Department.Address.State;
				departmentAddress.PostalCode = model.Department.Address.PostalCode;
				departmentAddress.Country = model.Department.Address.Country;
			}
			else
				departmentAddress = model.Department.Address;

			model.User = _usersService.GetUserById(UserId);
			model.SetUsers(await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, false, true),
				await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId));

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

			if (!String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLatitude) && !LocationHelpers.IsValidLatitude(model.MapCenterGpsCoordinatesLatitude))
			{
				ModelState.AddModelError("MapCenterGpsCoordinatesLatitude", "Map Center Latitude value seems invalid, MUST be decimal format.");
			}

			if (!String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLongitude) && !LocationHelpers.IsValidLongitude(model.MapCenterGpsCoordinatesLongitude))
			{
				ModelState.AddModelError("MapCenterGpsCoordinatesLongitude", "Map Center Longitude value seems invalid, MUST be decimal format.");
			}

			if (ModelState.IsValid)
			{
				departmentAddress = await _addressService.SaveAddressAsync(departmentAddress, cancellationToken);
				d.AddressId = departmentAddress.AddressId;

				await _departmentsService.UpdateDepartmentAsync(d, cancellationToken);
				auditEvent.After = d.CloneJsonToString();

				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
				model.Message = "Department settings save successful, you may have to log out and log back in for everything to take effect.";

				model.Department = d;

				if (!String.IsNullOrWhiteSpace(model.MapZoomLevel))
					await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.MapZoomLevel, DepartmentSettingTypes.BigBoardMapZoomLevel, cancellationToken);

				if (!String.IsNullOrWhiteSpace(model.RefreshTime))
					await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.RefreshTime, DepartmentSettingTypes.BigBoardPageRefresh, cancellationToken);

				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.MapHideUnavailable.ToString(), DepartmentSettingTypes.BigBoardHideUnavailable,
					cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.DisableAutoAvailable.ToString(), DepartmentSettingTypes.DisabledAutoAvailable,
					cancellationToken);

				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.PersonnelSort.ToString(), DepartmentSettingTypes.PersonnelSortOrder,
					cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.UnitsSort.ToString(), DepartmentSettingTypes.UnitsSortOrder, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.CallsSort.ToString(), DepartmentSettingTypes.CallsSortOrder, cancellationToken);

				if (!String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLatitude) && !String.IsNullOrWhiteSpace(model.MapCenterGpsCoordinatesLongitude))
					await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, StringHelpers.SanitizeCoordinatesString(model.MapCenterGpsCoordinatesLatitude) + "," + StringHelpers.SanitizeCoordinatesString(model.MapCenterGpsCoordinatesLongitude),
						DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates, cancellationToken);

				model.SuppressStaffingInfo = new DepartmentSuppressStaffingInfo();

				if (form.ContainsKey("staffingLevelsToSupress") && form["staffingLevelsToSupress"].Any())
				{
					model.SuppressStaffingInfo.StaffingLevelsToSupress.AddRange(form["staffingLevelsToSupress"].ToString().Split(char.Parse(",")).Select(x => int.Parse(x)).ToList());
				}

				model.SuppressStaffingInfo.EnableSupressStaffing = model.EnableStaffingSupress;

				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, ObjectSerialization.Serialize(model.SuppressStaffingInfo), DepartmentSettingTypes.StaffingSuppressStaffingLevels, cancellationToken);

				if (!String.IsNullOrWhiteSpace(model.MapCenterPointAddressAddress1))
				{
					var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);

					if (address == null)
					{
						Address newAddress = new Address();
						newAddress.Address1 = model.MapCenterPointAddressAddress1;
						newAddress.City = model.MapCenterPointAddressCity;
						newAddress.State = model.MapCenterPointAddressState;
						newAddress.PostalCode = model.MapCenterPointAddressPostalCode;
						newAddress.Country = model.MapCenterPointAddressCountry;

						newAddress = await _addressService.SaveAddressAsync(newAddress, cancellationToken);
						await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, newAddress.AddressId.ToString(), DepartmentSettingTypes.BigBoardMapCenterAddress,
							cancellationToken);
					}
					else
					{
						address.Address1 = model.MapCenterPointAddressAddress1;
						address.City = model.MapCenterPointAddressCity;
						address.State = model.MapCenterPointAddressState;
						address.PostalCode = model.MapCenterPointAddressPostalCode;
						address.Country = model.MapCenterPointAddressCountry;

						await _addressService.SaveAddressAsync(address, cancellationToken);
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

					await _scheduledTasksService.DeleteDepartmentStaffingResetJobAsync(model.Department.DepartmentId, cancellationToken);
					await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);
				}
				else
				{
					model.ResetStaffingTo = (int)UserStateTypes.Available;
					model.TimeToResetStaffing = String.Empty;
					await _scheduledTasksService.DeleteDepartmentStaffingResetJobAsync(model.Department.DepartmentId, cancellationToken);
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

					await _scheduledTasksService.DeleteDepartmentStatusResetJob(model.Department.DepartmentId, cancellationToken);
					await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);
				}
				else
				{
					model.ResetStatusTo = (int)ActionTypes.StandingBy;
					model.TimeToResetStatus = String.Empty;
					await _scheduledTasksService.DeleteDepartmentStatusResetJob(model.Department.DepartmentId, cancellationToken);
				}

				_eventAggregator.SendMessage<DepartmentSettingsChangedEvent>(new DepartmentSettingsChangedEvent() { DepartmentId = DepartmentId });

				await _departmentsService.InvalidateAllDepartmentsCache(DepartmentId);

				return RedirectToAction("Settings", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Api()
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var activeCallRssKey = await _departmentSettingsService.GetRssKeyForDepartmentAsync(DepartmentId);
			if (!String.IsNullOrWhiteSpace(activeCallRssKey))
				model.ActiveCallRssKey = activeCallRssKey;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ProvisionApiKey(CancellationToken cancellationToken)
		{
			DepartmentSettingsModel model = new DepartmentSettingsModel();

			Department d = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			d.ApiKey = Guid.NewGuid().ToString("N");
			await _departmentsService.UpdateDepartmentAsync(d, cancellationToken);

			model.Department = d;

			return RedirectToAction("Api", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ProvisionApiKeyAsync(CancellationToken cancellationToken)
		{
			Department d = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			d.ApiKey = Guid.NewGuid().ToString("N");
			await _departmentsService.UpdateDepartmentAsync(d, cancellationToken);

			return Content(d.ApiKey);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ProvisionActiveCallRssKey(CancellationToken cancellationToken)
		{
			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, Guid.NewGuid().ToString("N"), DepartmentSettingTypes.RssFeedKeyForActiveCalls,
				cancellationToken);

			return RedirectToAction("Api", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Address()
		{
			SettingsAddressModel model = new SettingsAddressModel();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (model.Department.Address == null)
			{
				if (model.Department.AddressId.HasValue)
					model.Department.Address = await _addressService.GetAddressByIdAsync(model.Department.AddressId.Value);
				else
					model.Department.Address = new Address();
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Address(SettingsAddressModel model, CancellationToken cancellationToken)
		{
			Address address = null;

			if (model.Department.Address.AddressId != 0)
			{
				address = await _addressService.GetAddressByIdAsync(model.Department.Address.AddressId);
				address.AddressId = model.Department.Address.AddressId;
				address.Address1 = model.Department.Address.Address1;
				address.City = model.Department.Address.City;
				address.State = model.Department.Address.State;
				address.PostalCode = model.Department.Address.PostalCode;
				address.Country = model.Department.Address.Country;
			}
			else
				address = model.Department.Address;

			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);


			if (ModelState.IsValid)
			{
				//using (var scope = new TransactionScope())
				//{
				address = await _addressService.SaveAddressAsync(address, cancellationToken);

				model.Department.AddressId = address.AddressId;
				await _departmentsService.UpdateDepartmentAsync(model.Department, cancellationToken);

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
		public async Task<IActionResult> SetUserResponding(string userId)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
				(int)ActionTypes.Responding);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SetUserNotResponding(string userId)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
				(int)ActionTypes.NotResponding);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SetUserStandingBy(string userId)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
				(int)ActionTypes.StandingBy);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SetUserOnScene(string userId)
		{
			await _actionLogsService.SetUserActionAsync(userId, (await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId,
				(int)ActionTypes.OnScene);

			return RedirectToAction("Index", "Personnel", new { area = "User" });
		}

		#endregion User States

		#region Call Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> CallSettings()
		{
			var model = new CallSettingsView();
			model.EmailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var internalDispatchEmail = await _departmentSettingsService.GetDispatchEmailForDepartmentAsync(DepartmentId);
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

			var callPruning = await _departmentsService.GetDepartmentCallPruningSettingsAsync(DepartmentId);

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
		public async Task<IActionResult> CallSettings(CallSettingsView model, CancellationToken cancellationToken)
		{
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var internalDispatchEmail = await _departmentSettingsService.GetDispatchEmailForDepartmentAsync(DepartmentId);
			if (!String.IsNullOrWhiteSpace(internalDispatchEmail))
				model.InternalDispatchEmail = $"{internalDispatchEmail}@{Config.InboundEmailConfig.DispatchDomain}";

			//if (model.CallType <= 0)
			//	ModelState.AddModelError("", "You need to select a email type from the list. If you don't see your email type, contact us to get it added.");

			//if (String.IsNullOrWhiteSpace(model.EmailSettings.Hostname) && !String.IsNullOrWhiteSpace(_emailService.TestEmailSettings(model.EmailSettings)))
			//	ModelState.AddModelError("", "Call Email Settings appear incorrect. Check your email settings and try again.");

			if (ModelState.IsValid)
			{
				DepartmentCallEmail emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(DepartmentId);
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

				model.EmailSettings = await _departmentsService.SaveDepartmentEmailSettingsAsync(emailSettings, cancellationToken);

				DepartmentCallPruning pruningSettings = await _departmentsService.GetDepartmentCallPruningSettingsAsync(DepartmentId);

				if (pruningSettings == null)
					pruningSettings = new DepartmentCallPruning();

				pruningSettings.DepartmentId = DepartmentId;
				pruningSettings.PruneEmailImportedCalls = model.PruneEmailCalls;
				pruningSettings.EmailImportCallPruneInterval = model.MinutesTillPrune;

				pruningSettings = await _departmentsService.SaveDepartmentCallPruningAsync(pruningSettings, cancellationToken);

				if (pruningSettings.PruneEmailImportedCalls.HasValue)
					model.PruneEmailCalls = pruningSettings.PruneEmailImportedCalls.Value;

				if (pruningSettings.EmailImportCallPruneInterval.HasValue)
					model.MinutesTillPrune = pruningSettings.EmailImportCallPruneInterval.Value;

				await _departmentsService.InvalidateAllDepartmentsCache(DepartmentId);

				model.Message = "Email settings were successfully saved.";
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetCallEmailTypes()
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
					case CallEmailTypes.FourPartPipe:
						CallEmailTypesForJson fourPartPipe = new CallEmailTypesForJson();
						fourPartPipe.Id = (int)v;
						fourPartPipe.Name = "Four Part Pipe";
						fourPartPipe.Code = v.ToString();
						callEmailTypes.Add(fourPartPipe);
						break;
					case CallEmailTypes.RandR:
						CallEmailTypesForJson randr = new CallEmailTypesForJson();
						randr.Id = (int)v;
						randr.Name = "R&R";
						randr.Code = v.ToString();
						callEmailTypes.Add(randr);
						break;
					case CallEmailTypes.Active911:
						CallEmailTypesForJson active911 = new CallEmailTypesForJson();
						active911.Id = (int)v;
						active911.Name = "Active911";
						active911.Code = v.ToString();
						callEmailTypes.Add(active911);
						break;
					case CallEmailTypes.OttawaCounty:
						CallEmailTypesForJson ottawaCounty = new CallEmailTypesForJson();
						ottawaCounty.Id = (int)v;
						ottawaCounty.Name = "OttawaCounty";
						ottawaCounty.Code = v.ToString();
						callEmailTypes.Add(ottawaCounty);
						break;
					case CallEmailTypes.OttawaKingstonToronto:
						CallEmailTypesForJson ottawaKingstonToronto = new CallEmailTypesForJson();
						ottawaKingstonToronto.Id = (int)v;
						ottawaKingstonToronto.Name = "OttawaKingstonToronto";
						ottawaKingstonToronto.Code = v.ToString();
						callEmailTypes.Add(ottawaKingstonToronto);
						break;
				}
			}

			return Json(callEmailTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ClearDepartmentCache()
		{
			CqrsEvent clearDepartmentCacheEvent = new CqrsEvent();
			clearDepartmentCacheEvent.Type = (int)CqrsEventTypes.ClearDepartmentCache;
			clearDepartmentCacheEvent.Data = DepartmentId.ToString();

			await _cqrsProvider.EnqueueCqrsEventAsync(clearDepartmentCacheEvent);

			return Json("true");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteCallEmailSettings(CancellationToken cancellationToken)
		{
			await _departmentsService.DeleteDepartmentEmailSettingsAsync(DepartmentId, cancellationToken);

			return RedirectToAction("CallSettings");
		}

		#endregion Call Settings

		#region Unit Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> UnitSettings()
		{
			UnitSettingsView model = new UnitSettingsView();
			model.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			return View(model);
		}
		#endregion Unit Settings

		#region Types

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Types()
		{
			var model = new DepartmentTypesView();
			model.CertificationTypes = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);
			model.UnitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
			model.DocumentCategories = await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId);
			model.NoteCategories = await _notesService.GetAllCategoriesByDepartmentIdAsync(DepartmentId);
			model.ContactNoteTypes = await _contactsService.GetContactNoteTypesByDepartmentIdAsync(DepartmentId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;

			model.CallPriorites = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);

			if (model.CallPriorites == null || !model.CallPriorites.Any())
			{
				model.CallPriorites = _callsService.GetDefaultCallPriorities();

				//Sounds don't need to come back to the UI
				foreach (var priority in model.CallPriorites)
				{
					priority.PushNotificationSound = null;
					priority.ShortNotificationSound = null;
				}
			}

			return View(model);
		}

		#endregion Types

		#region Text Messaging

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> TextSettings()
		{
			var model = new TextSetupModel();

			model.CanProvisionNumber = await _limitsService.CanDepartmentProvisionNumberAsync(DepartmentId);
			model.DepartmentTextToCallNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			model.DepartmentTextToCallSourceNumbers = await _departmentSettingsService.GetTextToCallSourceNumbersForDepartmentAsync(DepartmentId);
			model.EnableTextToCall = await _departmentSettingsService.GetDepartmentIsTextCallImportEnabledAsync(DepartmentId);
			model.EnableTextCommand = await _departmentSettingsService.GetDepartmentIsTextCommandEnabledAsync(DepartmentId);

			var textImportFormat = await _departmentSettingsService.GetTextToCallImportFormatForDepartmentAsync(DepartmentId);
			if (textImportFormat.HasValue)
				model.TextCallType = textImportFormat.Value;

			return View(model);
		}


		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> TextSettings(TextSetupModel model, CancellationToken cancellationToken)
		{
			model.CanProvisionNumber = await _limitsService.CanDepartmentProvisionNumberAsync(DepartmentId);
			model.DepartmentTextToCallNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);

			var textImportFormat = await _departmentSettingsService.GetTextToCallImportFormatForDepartmentAsync(DepartmentId);
			if (textImportFormat.HasValue)
				model.TextCallType = textImportFormat.Value;

			if (model.EnableTextCommand && model.EnableTextToCall && String.IsNullOrWhiteSpace(model.DepartmentTextToCallSourceNumbers))
				ModelState.AddModelError("DepartmentTextToCallSourceNumbers",
					"You have enabled Text-To-Call and Text Command, to prevent all personel command texts being imported as calls you must supply the phone numbers that text dispatches will orginiate from.");

			if (ModelState.IsValid)
			{
				if (!String.IsNullOrWhiteSpace(model.DepartmentTextToCallSourceNumbers))
					await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.DepartmentTextToCallSourceNumbers, DepartmentSettingTypes.TextToCallSourceNumbers,
						cancellationToken);
				else
					await _departmentSettingsService.DeleteSettingAsync(DepartmentId, DepartmentSettingTypes.TextToCallSourceNumbers, cancellationToken);

				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.TextCallType.ToString(), DepartmentSettingTypes.TextToCallImportFormat,
					cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.EnableTextToCall.ToString(), DepartmentSettingTypes.EnableTextToCall,
					cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.EnableTextCommand.ToString(), DepartmentSettingTypes.EnableTextCommand,
					cancellationToken);
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> GetAvailableNumbers(string country, string areaCode)
		{
			return Json(await _numbersService.GetAvailableNumbers(country, areaCode));
		}

		[HttpGet]
		public async Task<IActionResult> ProvisionNumber(string msisdn, string country, CancellationToken cancellationToken)
		{
			if (!await _limitsService.CanDepartmentProvisionNumberAsync(DepartmentId))
				return RedirectToAction("Unauthorized", "Public", new { Area = "" });

			var success = await _numbersService.ProvisionNumberAsync(DepartmentId, msisdn, country);

			if (success)
			{
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, "0", DepartmentSettingTypes.TextToCallImportFormat, cancellationToken);
				return RedirectToAction("TextSettings");
			}

			return RedirectToAction("ProvisionFailed");
		}

		[HttpGet]
		public async Task<IActionResult> ProvisionDefaultNumberAsync(string country, string areaCode, CancellationToken cancellationToken)
		{
			if (!await _limitsService.CanDepartmentProvisionNumberAsync(DepartmentId))
				return RedirectToAction("Unauthorized", "Public", new { Area = "" });

			var numbers = await _numbersService.GetAvailableNumbers(country, areaCode);

			if (numbers.Count > 0)
			{
				var success = await _numbersService.ProvisionNumberAsync(DepartmentId, numbers.First().msisdn, country);

				if (success)
				{
					await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, "0", DepartmentSettingTypes.TextToCallImportFormat, cancellationToken);

					return Content(numbers.First().msisdn);
				}
			}

			return Content("Could not provision number");
		}

		public async Task<IActionResult> ProvisionFailed()
		{
			return View();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetCallTextTypes()
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
		public async Task<IActionResult> GetStationsForGrid()
		{
			var stations = new List<StationJson>();

			var groups = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> GetDepartmentTypes()
		{
			var model = new RegisterViewModel();

			return Json(new SelectList(model.DepartmentTypes));
		}

		#endregion Stations Grid

		#region Recipients

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> GetRecipientsForGrid(int filter = 0, bool filterSelf = false, bool filterNotInGroup = false, int ignoreGroupId = 0)
		{
			var result = new List<RecipientJson>();

			var stations = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> GetSubscriptionLimitWarning()
		{
			if (!String.IsNullOrWhiteSpace(Config.NoticeConfig.DashboardToastNotice))
				return Json(new { title = "System Notice", message = Config.NoticeConfig.DashboardToastNotice });

			if (!await _limitsService.ValidateDepartmentIsWithinLimitsAsync(DepartmentId))
				return Json(new
				{
					title = "Department Limits", message = "The department is at or has exceeded some limits for the current subscription plan. Some functionality may be impacted."
				});

			return Json(new { title = "", message = "" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<PartialViewResult> TopIconsArea()
		{
			TopIconsModel model = new TopIconsModel();
			await model.SetMessages(UserId, DepartmentId);

			return PartialView("_TopIconsPartial", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> UpgradeButton()
		{
			if (!Config.SystemBehaviorConfig.RedirectHomeToLogin)
			{
				var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && plan != null && plan.PlanId == 1)
					return PartialView("_TopUpgradePartial");
			}

			return Content("<span></span>");
		}

		#endregion Aync Site Parts

		#region Async Validation Methods

		//[HttpPost]
		//[Authorize(Policy = ResgridResources.Department_View)]
		//public async Task<IActionResult> TestEmailSettings(EmailTestInput input)
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

		//		returnText = await _emailService.TestEmailSettings(email);
		//	}
		//	else
		//	{
		//		returnText = "Please supply a hostname, username and password.";
		//	}

		//	return Content(returnText);
		//}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> ValidateAddress(Address address)
		{
			string returnText = "";

			//SJ: Disabling for now.
			var result = await _addressService.IsAddressValidAsync(address);

			if (result.ServiceSuccess && !result.AddressValid)
				returnText = "Address looks like it might be invalid. Verify it before you continue.";

			return Content(returnText);
		}

		#endregion Async Validation Methods

		#region Setup Wizard

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> SetupWizard()
		{
			var model = new SetupWizardView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.CanProvisionNumber = await _limitsService.CanDepartmentProvisionNumberAsync(DepartmentId);
			model.DepartmentTextToCallNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			model.DepartmentTextToCallSourceNumbers = await _departmentSettingsService.GetTextToCallSourceNumbersForDepartmentAsync(DepartmentId);
			model.EmailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(DepartmentId);
			model.DepartmentEmailAddress = await _departmentSettingsService.GetDispatchEmailForDepartmentAsync(DepartmentId);

			return View("_SetupWizard", model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> SubmitSetupWizard([FromBody] SetupWizardFormPayload payload, CancellationToken cancellationToken)
		{
			var formCollection = JsonConvert.DeserializeObject<Dictionary<string, string>>(payload.setupWizardForm);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			department.TimeZone = formCollection["Department.TimeZone"];

			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, formCollection["DisableAutoAvailable"], DepartmentSettingTypes.DisabledAutoAvailable,
				cancellationToken);

			Address address = null;
			if (department.AddressId.HasValue)
				address = await _addressService.GetAddressByIdAsync(department.AddressId.Value);
			else
				address = new Address();

			address.Address1 = formCollection["Department.Address.Address1"];
			address.City = formCollection["Department.Address.City"];
			address.State = formCollection["Department.Address.State"];
			address.PostalCode = formCollection["Department.Address.PostalCode"];
			address.Country = formCollection["Department.Address.Country"];

			address = await _addressService.SaveAddressAsync(address, cancellationToken);
			department.AddressId = address.AddressId;

			await _departmentsService.SaveDepartmentAsync(department, cancellationToken);

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

				stationAddress = await _addressService.SaveAddressAsync(stationAddress, cancellationToken);
				station.AddressId = stationAddress.AddressId;

				newStations.Add(await _departmentGroupsService.SaveAsync(station, cancellationToken));
			}

			int unitsCount = int.Parse(formCollection["UnitCount"]);
			var newUnits = new List<Unit>();
			for (int i = 1; i <= unitsCount; i++)
			{
				var unit = new Unit();
				unit.Name = formCollection["unit_" + i];
				unit.DepartmentId = DepartmentId;

				newUnits.Add(await _unitsService.SaveUnitAsync(unit, cancellationToken));
			}

			if (formCollection["CallImportOption"] == "Email Call Importing")
			{
				DepartmentCallEmail emailSettings = await _departmentsService.GetDepartmentEmailSettingsAsync(DepartmentId);
				if (emailSettings == null)
					emailSettings = new DepartmentCallEmail();

				emailSettings.DepartmentId = DepartmentId;
				emailSettings.Hostname = formCollection["EmailSettings.Hostname"];
				emailSettings.Password = formCollection["EmailSettings.Password"];
				emailSettings.Port = int.Parse(formCollection["EmailSettings.Port"]);
				emailSettings.Username = formCollection["EmailSettings.Username"];
				emailSettings.FormatType = (int)CallEmailTypes.Generic;
				emailSettings.UseSsl = bool.Parse(formCollection["EmailSettings.UseSsl"]);

				await _departmentsService.SaveDepartmentEmailSettingsAsync(emailSettings, cancellationToken);
			}


			return new JsonResult("{}");
		}

		#endregion Setup Wizard

		#region Shift Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ShiftSettings()
		{
			var model = new ShiftSettingsView();
			model.AllowSignupsForMultipleShiftGroups = await _departmentSettingsService.GetAllowSignupsForMultipleShiftGroupsAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ShiftSettings(ShiftSettingsView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.AllowSignupsForMultipleShiftGroups.ToString(),
					DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups, cancellationToken);

				model.SaveSuccess = true;
				return View(model);
			}

			model.SaveSuccess = false;
			return View(model);
		}

		#endregion Shift Settings

		#region Dispatch Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DispatchSettings()
		{
			var model = new DispatchSettingsView();

			var actionLogs = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			if (actionLogs == null)
			{
				var statuses = model.UserStatusTypes.ToSelectListInt().ToList();
				statuses.Insert(0, new SelectListItem() { Value = "-1", Text = "Default" });
				model.StatusLevels = new SelectList(statuses, "Value", "Text");
			}
			else
			{
				List<CustomStateDetail> statuses = new List<CustomStateDetail>();
				statuses.Add(new CustomStateDetail() { CustomStateDetailId = -1, ButtonText = "Default" });
				statuses.AddRange(actionLogs.GetActiveDetails());

				model.StatusLevels = new SelectList(statuses, "CustomStateDetailId", "ButtonText");
			}

			model.DispatchShiftInsteadOfGroup = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(DepartmentId);
			model.AutoSetStatusForShiftPersonnel = await _departmentSettingsService.GetAutoSetStatusForShiftDispatchPersonnelAsync(DepartmentId);
			model.ShiftDispatchStatus = await _departmentSettingsService.GetShiftCallDispatchPersonnelStatusToSetAsync(DepartmentId);
			model.ShiftClearStatus = await _departmentSettingsService.GetShiftCallReleasePersonnelStatusToSetAsync(DepartmentId);
			model.UnitDispatchAlsoDispatchToAssignedPersonnel = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(DepartmentId);
			model.UnitDispatchAlsoDispatchToGroup = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DispatchSettings(DispatchSettingsView model, CancellationToken cancellationToken)
		{
			var actionLogs = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			if (actionLogs == null)
			{
				var statuses = model.UserStatusTypes.ToSelectListInt().ToList();
				statuses.Insert(0, new SelectListItem() { Value = "-1", Text = "Default" });
				model.StatusLevels = new SelectList(statuses, "Value", "Text");
			}
			else
			{
				List<CustomStateDetail> statuses = new List<CustomStateDetail>();
				statuses.Add(new CustomStateDetail() { CustomStateDetailId = -1, ButtonText = "Default" });
				statuses.AddRange(actionLogs.GetActiveDetails());

				model.StatusLevels = new SelectList(statuses, "CustomStateDetailId", "ButtonText");
			}

			if (ModelState.IsValid)
			{
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.DispatchShiftInsteadOfGroup.ToString(),
					DepartmentSettingTypes.DispatchShiftInsteadOfGroup, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.AutoSetStatusForShiftPersonnel.ToString(),
					DepartmentSettingTypes.AutoSetStatusForShiftDispatchPersonnel, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.ShiftDispatchStatus.ToString(),
					DepartmentSettingTypes.ShiftCallDispatchPersonnelStatusToSet, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.ShiftClearStatus.ToString(),
					DepartmentSettingTypes.ShiftCallReleasePersonnelStatusToSet, cancellationToken);

				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.UnitDispatchAlsoDispatchToAssignedPersonnel.ToString(),
					DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.UnitDispatchAlsoDispatchToGroup.ToString(),
					DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup, cancellationToken);

				model.SaveSuccess = true;
				return View(model);
			}

			model.SaveSuccess = false;
			return View(model);
		}

		#endregion Dispatch Settings

		#region Mapping Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> MappingSettings()
		{
			var model = new MappingSettingsView();

			model.PersonnelLocationTTL = await _departmentSettingsService.GetMappingPersonnelLocationTTLAsync(DepartmentId);
			model.UnitLocationTTL = await _departmentSettingsService.GetMappingUnitLocationTTLAsync(DepartmentId);
			model.PersonnelAllowStatusWithNoLocationToOverwrite = await _departmentSettingsService.GetMappingPersonnelAllowStatusWithNoLocationToOverwriteAsync(DepartmentId);
			model.UnitAllowStatusWithNoLocationToOverwrite = await _departmentSettingsService.GetMappingUnitAllowStatusWithNoLocationToOverwriteAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> MappingSettings(MappingSettingsView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.PersonnelLocationTTL.ToString(),
					DepartmentSettingTypes.MappingPersonnelLocationTTL, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.UnitLocationTTL.ToString(),
					DepartmentSettingTypes.MappingUnitLocationTTL, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.PersonnelAllowStatusWithNoLocationToOverwrite.ToString(),
					DepartmentSettingTypes.MappingPersonnelAllowStatusWithNoLocationToOverwrite, cancellationToken);
				await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, model.UnitAllowStatusWithNoLocationToOverwrite.ToString(),
					DepartmentSettingTypes.MappingUnitAllowStatusWithNoLocationToOverwrite, cancellationToken);

				model.SaveSuccess = true;
				return View(model);
			}

			model.SaveSuccess = false;
			return View(model);
		}

		#endregion Mapping Settings

		#region Delete Department

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteDepartment()
		{
			DeleteDepartmentView model = new DeleteDepartmentView();
			model.CurrentDeleteRequest = await _queueService.GetPendingDeleteDepartmentQueueItemAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (model.CurrentDeleteRequest != null)
			{
				model.Profile = await _userProfileService.GetProfileByUserIdAsync(model.CurrentDeleteRequest.QueuedByUserId);
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteDepartment(DeleteDepartmentView model, CancellationToken cancellationToken)
		{
			if (model.AreYouSure == false)
				ModelState.AddModelError("AreYouSure", "You need to confirm the delete.");

			if (ModelState.IsValid)
			{
				var result = await _deleteService.DeleteDepartment(DepartmentId, UserId, IpAddressHelper.GetRequestIP(Request, true),
					$"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

				var stripeCustomer = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);
				var currentSub = await _subscriptionsService.GetActiveStripeSubscriptionAsync(stripeCustomer);
				var pttSub = await _subscriptionsService.GetActivePTTStripeSubscriptionAsync(stripeCustomer);

				if (pttSub != null)
				{
					PlanAddon addon = new PlanAddon();
					await _subscriptionsService.ModifyPTTAddonSubscriptionAsync(stripeCustomer, 0, addon);
				}

				if (currentSub != null)
				{
					await _subscriptionsService.CancelSubscriptionAsync(stripeCustomer);
				}

				return RedirectToAction("Settings", "Department", new { area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> CancelDepartmentDeleteRequest(CancellationToken cancellationToken)
		{
			var queueItem = await _queueService.GetPendingDeleteDepartmentQueueItemAsync(DepartmentId);

			if (queueItem != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.Before = null;
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.DeleteDepartmentRequestedCancelled;
				auditEvent.After = null;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				var profile = await _userProfileService.GetProfileByUserIdAsync(UserId);

				await _queueService.CancelPendingDepartmentDeletionRequest(DepartmentId, profile.FullName.AsFirstNameLastName, cancellationToken);
			}

			return RedirectToAction("Settings", "Department", new { area = "User" });
		}

		#endregion Delete Department

		#region Module Settings
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ModuleSettings()
		{
			var model = new DepartmentModulesSettingView();
			model.Modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);

			model.MessagingEnabled = !model.Modules.MessagingDisabled;
			model.MappingEnabled = !model.Modules.MappingDisabled;
			model.ShiftsEnabled = !model.Modules.ShiftsDisabled;
			model.LogsEnabled = !model.Modules.LogsDisabled;
			model.ReportsEnabled = !model.Modules.ReportsDisabled;
			model.DocumentsEnabled = !model.Modules.DocumentsDisabled;
			model.CalendarEnabled = !model.Modules.CalendarDisabled;
			model.NotesEnabled = !model.Modules.NotesDisabled;
			model.TrainingEnabled = !model.Modules.TrainingDisabled;
			model.InventoryEnabled = !model.Modules.InventoryDisabled;
			model.MaintenanceEnabled = !model.Modules.MaintenanceDisabled;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ModuleSettings(DepartmentModulesSettingView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);

				modules.MessagingDisabled = !model.MessagingEnabled;
				modules.MappingDisabled = !model.MappingEnabled;
				modules.ShiftsDisabled = !model.ShiftsEnabled;
				modules.LogsDisabled = !model.LogsEnabled;
				modules.ReportsDisabled = !model.ReportsEnabled;
				modules.DocumentsDisabled = !model.DocumentsEnabled;
				modules.CalendarDisabled = !model.CalendarEnabled;
				modules.NotesDisabled = !model.NotesEnabled;
				modules.TrainingDisabled = !model.TrainingEnabled;
				modules.InventoryDisabled = !model.InventoryEnabled;
				modules.MaintenanceDisabled = !model.MaintenanceEnabled;

				await _departmentSettingsService.SetDepartmentModuleSettingsAsync(DepartmentId, modules, cancellationToken);

				model.SaveSuccess = true;
				return View(model);
			}

			model.SaveSuccess = false;
			return View(model);
		}
		#endregion Module Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetPrinterNetPrinters(string key)
		{
			return Json(await _printerProvider.GetPrinters(key));
		}
	}

	public class SetupWizardFormPayload
	{
		public string setupWizardForm { get; set; }
	}
}
