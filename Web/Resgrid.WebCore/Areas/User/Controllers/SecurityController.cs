using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class SecurityController : SecureBaseController
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuditService _auditService;
		private readonly IPermissionsService _permissionsService;
		private readonly IEventAggregator _eventAggregator;

		public SecurityController(IDepartmentsService departmentsService, IAuditService auditService,
			IPermissionsService permissionsService, IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_auditService = auditService;
			_permissionsService = permissionsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<IActionResult> Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			var model = new PermissionsView();

			var permissions = await _permissionsService.GetAllPermissionsForDepartmentAsync(DepartmentId);

			int val = (int)PermissionTypes.AddPersonnel;
			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.AddPersonnel))
				model.AddUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.AddPersonnel).Action;

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.RemovePersonnel))
				model.RemoveUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.RemovePersonnel).Action;

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCall))
				model.CreateCall = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCall).Action;
			else
				model.CreateCall = 3;

			var userAddPermissions = new List<dynamic>();
			userAddPermissions.Add(new { Id = 0, Name = "Department Admins" });
			userAddPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			model.AddUserPermissions = new SelectList(userAddPermissions, "Id", "Name");

			var userDeletePermissions = new List<dynamic>();
			userDeletePermissions.Add(new { Id = 0, Name = "Department Admins" });
			userDeletePermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			model.RemoveUserPermissions = new SelectList(userDeletePermissions, "Id", "Name");

			var createCallPermissions = new List<dynamic>();
			createCallPermissions.Add(new { Id = 3, Name = "Everyone" });
			createCallPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createCallPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createCallPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateCallPermissions = new SelectList(createCallPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateTraining))
				model.CreateTraining = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateTraining).Action;

			var createTrainingPermissions = new List<dynamic>();
			createTrainingPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createTrainingPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createTrainingPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateTrainingPermissions = new SelectList(createTrainingPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateDocument))
				model.CreateDocument = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateDocument).Action;
			else
				model.CreateDocument = 3;

			var createDocumentPermissions = new List<dynamic>();
			createDocumentPermissions.Add(new { Id = 3, Name = "Everyone" });
			createDocumentPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createDocumentPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createDocumentPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateDocumentPermissions = new SelectList(createDocumentPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry))
				model.CreateCalendarEntry = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry).Action;
			else
				model.CreateCalendarEntry = 3;

			var createCalendarEntryPermissions = new List<dynamic>();
			createCalendarEntryPermissions.Add(new { Id = 3, Name = "Everyone" });
			createCalendarEntryPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createCalendarEntryPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createCalendarEntryPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateCalendarEntryPermissions = new SelectList(createCalendarEntryPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateNote))
				model.CreateNote = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateNote).Action;
			else
				model.CreateNote = 3;

			var createNotePermissions = new List<dynamic>();
			createNotePermissions.Add(new { Id = 3, Name = "Everyone" });
			createNotePermissions.Add(new { Id = 0, Name = "Department Admins" });
			createNotePermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createNotePermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateNotePermissions = new SelectList(createNotePermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateLog))
				model.CreateLog = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateLog).Action;
			else
				model.CreateLog = 3;

			var createLogPermissions = new List<dynamic>();
			createLogPermissions.Add(new { Id = 3, Name = "Everyone" });
			createLogPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createLogPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createLogPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateLogPermissions = new SelectList(createLogPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateShift))
				model.CreateShift = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateShift).Action;

			var createShiftPermissions = new List<dynamic>();
			createShiftPermissions.Add(new { Id = 0, Name = "Department Admins" });
			createShiftPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createShiftPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateShiftPermissions = new SelectList(createShiftPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo))
				model.ViewPersonalInfo = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo).Action;
			else
				model.ViewPersonalInfo = 3;

			var viewPersonalInfoPermissions = new List<dynamic>();
			viewPersonalInfoPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewPersonalInfoPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewPersonalInfoPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewPersonalInfoPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewPersonalInfoPermissions = new SelectList(viewPersonalInfoPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.AdjustInventory))
				model.AdjustInventory = permissions.First(x => x.PermissionType == (int)PermissionTypes.AdjustInventory).Action;
			else
				model.AdjustInventory = 3;

			var adjustInventoryPermissions = new List<dynamic>();
			adjustInventoryPermissions.Add(new { Id = 3, Name = "Everyone" });
			adjustInventoryPermissions.Add(new { Id = 0, Name = "Department Admins" });
			adjustInventoryPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			adjustInventoryPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.AdjustInventoryPermissions = new SelectList(adjustInventoryPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CanSeePersonnelLocations))
			{
				model.ViewPersonnelLocation = permissions.First(x => x.PermissionType == (int)PermissionTypes.CanSeePersonnelLocations).Action;
				model.LockViewPersonneLocationToGroup = permissions.First(x => x.PermissionType == (int)PermissionTypes.CanSeePersonnelLocations).LockToGroup;
			}
			else
				model.ViewPersonnelLocation = 3;

			var viewPersonnelLocationPermissions = new List<dynamic>();
			viewPersonnelLocationPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewPersonnelLocationPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewPersonnelLocationPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewPersonnelLocationPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewPersonnelLocationPermissions = new SelectList(viewPersonnelLocationPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CanSeeUnitLocations))
			{
				model.ViewUnitLocation = permissions.First(x => x.PermissionType == (int)PermissionTypes.CanSeeUnitLocations).Action;
				model.LockViewUnitLocationToGroup = permissions.First(x => x.PermissionType == (int)PermissionTypes.CanSeeUnitLocations).LockToGroup;
			}
			else
				model.ViewUnitLocation = 3;

			var viewUnitLocationPermissions = new List<dynamic>();
			viewUnitLocationPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewUnitLocationPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewUnitLocationPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewUnitLocationPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewUnitLocationPermissions = new SelectList(viewUnitLocationPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateMessage))
				model.CreateMessage = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateMessage).Action;
			else
				model.CreateMessage = 3;

			var createMessagePermissions = new List<dynamic>();
			createMessagePermissions.Add(new { Id = 3, Name = "Everyone" });
			createMessagePermissions.Add(new { Id = 0, Name = "Department Admins" });
			createMessagePermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			createMessagePermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CreateMessagePermissions = new SelectList(createMessagePermissions, "Id", "Name");



			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewGroupUsers))
			{
				model.ViewGroupsUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewGroupUsers).Action;
				model.LockViewGroupsUsersToGroup = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewGroupUsers).LockToGroup;
			}
			else
				model.ViewGroupsUsers = 3;

			var viewGroupUsersPermissions = new List<dynamic>();
			viewGroupUsersPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewGroupUsersPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewGroupUsersPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewGroupUsersPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewGroupUsersPermissions = new SelectList(viewGroupUsersPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.DeleteCall))
				model.DeleteCall = permissions.First(x => x.PermissionType == (int)PermissionTypes.DeleteCall).Action;
			else
				model.DeleteCall = 3;

			var deleteCallPermissions = new List<dynamic>();
			deleteCallPermissions.Add(new { Id = 3, Name = "Everyone" });
			deleteCallPermissions.Add(new { Id = 0, Name = "Department Admins" });
			deleteCallPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			deleteCallPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.DeleteCallPermissions = new SelectList(deleteCallPermissions, "Id", "Name");


			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.CloseCall))
				model.CloseCall = permissions.First(x => x.PermissionType == (int)PermissionTypes.CloseCall).Action;
			else
				model.CloseCall = 3;

			var closeCallPermissions = new List<dynamic>();
			closeCallPermissions.Add(new { Id = 3, Name = "Everyone" });
			closeCallPermissions.Add(new { Id = 0, Name = "Department Admins" });
			closeCallPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			closeCallPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.CloseCallPermissions = new SelectList(closeCallPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.AddCallData))
				model.AddCallData = permissions.First(x => x.PermissionType == (int)PermissionTypes.AddCallData).Action;
			else
				model.AddCallData = 3;

			var addCallDataPermissions = new List<dynamic>();
			addCallDataPermissions.Add(new { Id = 3, Name = "Everyone" });
			addCallDataPermissions.Add(new { Id = 0, Name = "Department Admins" });
			addCallDataPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			addCallDataPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.AddCallDataPermissions = new SelectList(addCallDataPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewGroupUnits))
			{
				model.ViewGroupsUnits = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewGroupUnits).Action;
				model.LockViewGroupsUnitsToGroup = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewGroupUnits).LockToGroup;
			}
			else
				model.ViewGroupsUnits = 3;

			var viewGroupUnitsPermissions = new List<dynamic>();
			viewGroupUnitsPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewGroupUnitsPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewGroupUnitsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewGroupUnitsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewGrouUnitsPermissions = new SelectList(viewGroupUnitsPermissions, "Id", "Name");

			var viewContactsPermissions = new List<dynamic>();
			viewContactsPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewContactsPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewContactsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewContactsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewContactsPermissions = new SelectList(viewContactsPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactView))
				model.ViewContacts = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactView).Action;
			else
				model.ViewContacts = 3;

			var editContactsPermissions = new List<dynamic>();
			editContactsPermissions.Add(new { Id = 3, Name = "Everyone" });
			editContactsPermissions.Add(new { Id = 0, Name = "Department Admins" });
			editContactsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			editContactsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.EditContactsPermissions = new SelectList(editContactsPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactEdit))
				model.EditContacts = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactEdit).Action;
			else
				model.EditContacts = 3;

			var deleteContactsPermissions = new List<dynamic>();
			deleteContactsPermissions.Add(new { Id = 3, Name = "Everyone" });
			deleteContactsPermissions.Add(new { Id = 0, Name = "Department Admins" });
			deleteContactsPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			deleteContactsPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.DeleteContactsPermissions = new SelectList(deleteContactsPermissions, "Id", "Name");

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactDelete))
				model.DeleteContacts = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactDelete).Action;
			else
				model.DeleteContacts = 3;

			return View(model);
		}

		public async Task<IActionResult> Audits()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			return View();
		}

		public async Task<IActionResult> GetAuditLogsList()
		{
			var auditLogsJson = new List<AuditLogJson>();
			var auditLogs = await _auditService.GetAllAuditLogsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var auditLog in auditLogs)
			{
				var auditJson = new AuditLogJson();
				auditJson.AuditLogId = auditLog.AuditLogId;
				//auditJson.Name = UserHelper.GetFullNameForUser(null, auditLog.UserId);
				auditJson.Message = auditLog.Message;

				if (auditLog.LoggedOn.HasValue)
					auditJson.Timestamp = auditLog.LoggedOn.Value.TimeConverterToString(department);
				else
					auditJson.Timestamp = "Unknown";

				auditJson.Type = _auditService.GetAuditLogTypeString((AuditLogTypes)auditLog.LogType);

				auditLogsJson.Add(auditJson);
			}

			return Json(auditLogsJson);
		}

		public async Task<IActionResult> ViewAudit(int auditLogId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			var model = new ViewAuditLogView();
			model.AuditLog = await _auditService.GetAuditLogByIdAsync(auditLogId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Type = (AuditLogTypes)model.AuditLog.LogType;

			if (model.AuditLog.DepartmentId != DepartmentId)
				Unauthorized();


			return View(model);
		}

		#region Async

		[HttpGet]
		public async Task<IActionResult> SetPermission(int type, int perm, bool? lockToGroup)
		{
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var before = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, (PermissionTypes)type);
				var result = await _permissionsService.SetPermissionForDepartmentAsync(DepartmentId, UserId, (PermissionTypes)type, (PermissionActions)perm, null, lockToGroup.GetValueOrDefault());

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.PermissionsChanged;
				auditEvent.Before = before.CloneJsonToString();
				auditEvent.After = result.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				if (type == (int)PermissionTypes.CanSeePersonnelLocations)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewPersonnelLocations;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.CanSeeUnitLocations)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewUnitLocations;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.ViewGroupUnits)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewUnits;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.ViewGroupUsers)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewPersonnel;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}

				return new StatusCodeResult((int)HttpStatusCode.OK);
			}

			return new StatusCodeResult((int)HttpStatusCode.NotModified);
		}

		public async Task<IActionResult> SetPermissionData(int type, string data, bool? lockToGroup)
		{
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var before = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, (PermissionTypes)type);
				var result = await _permissionsService.SetPermissionForDepartmentAsync(DepartmentId, UserId, (PermissionTypes)type, (PermissionActions)before.Action, data, lockToGroup.GetValueOrDefault());

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.PermissionsChanged;
				auditEvent.Before = before.CloneJsonToString();
				auditEvent.After = result.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				if (type == (int)PermissionTypes.CanSeePersonnelLocations)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewPersonnelLocations;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.CanSeeUnitLocations)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewUnitLocations;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.ViewGroupUnits)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewUnits;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}
				else if (type == (int)PermissionTypes.ViewGroupUsers)
				{
					var securityEvent = new SecurityRefreshEvent();
					securityEvent.DepartmentId = DepartmentId;
					securityEvent.Type = SecurityCacheTypes.WhoCanViewPersonnel;
					_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
				}

				return new StatusCodeResult((int)HttpStatusCode.OK);
			}

			return new StatusCodeResult((int)HttpStatusCode.NotModified);
		}

		public async Task<IActionResult> GetRolesForPermission(int type)
		{
			var before = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, (PermissionTypes)type);

			if (before != null)
				return Json(before.Data);

			return Json("");
		}
		#endregion Async
	}
}
