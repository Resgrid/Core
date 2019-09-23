using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
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

		public IActionResult Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			var model = new PermissionsView();

			var permissions = _permissionsService.GetAllPermissionsForDepartment(DepartmentId);

			int val = (int)PermissionTypes.AddPersonnel;
			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.AddPersonnel))
				model.AddUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.AddPersonnel).Action;

			if (permissions.Any(x => x.PermissionType == (int)PermissionTypes.RemovePersonnel))
				model.RemoveUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.RemovePersonnel).Action;

			if (permissions.Any(x => x.PermissionType == (int) PermissionTypes.CreateCall))
				model.CreateCall = permissions.First(x => x.PermissionType == (int) PermissionTypes.CreateCall).Action;
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
				model.ViewGroupsUsers = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewGroupUsers).Action;
			else
				model.ViewGroupsUsers = 3;

			var viewGroupUsersPermissions = new List<dynamic>();
			viewGroupUsersPermissions.Add(new { Id = 3, Name = "Everyone" });
			viewGroupUsersPermissions.Add(new { Id = 0, Name = "Department Admins" });
			viewGroupUsersPermissions.Add(new { Id = 1, Name = "Department and Group Admins" });
			viewGroupUsersPermissions.Add(new { Id = 2, Name = "Department Admins and Select Roles" });
			model.ViewGroupUsersPermissions = new SelectList(viewGroupUsersPermissions, "Id", "Name");

			return View(model);
		}

		public IActionResult Audits()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			return View();
		}

		public IActionResult GetAuditLogsList()
		{
			var auditLogsJson = new List<AuditLogJson>();
			var auditLogs = _auditService.GetAllAuditLogsForDepartment(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

		public IActionResult DeleteAudit(int auditLogId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			_auditService.DeleteAuditLogById(auditLogId);

			return RedirectToAction("Audits");
		}

		[HttpPost]
		public IActionResult ClearSelectedAuditLogs(IFormCollection form)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			var auditLogIds = new List<int>();
			foreach (var key in form.Keys)
			{
				if (key.ToString().StartsWith("selectAuditLog_"))
				{
					var eventId = int.Parse(key.ToString().Replace("selectAuditLog_", ""));
					auditLogIds.Add(eventId);
				}
			}

			if (auditLogIds.Any())
				_auditService.DeleteSelectedAuditLogs(DepartmentId, auditLogIds);

			return RedirectToAction("Audits");
		}

		#region Async

		[HttpGet]
		public IActionResult SetPermission(int type, int perm, bool? lockToGroup)
		{
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var before = _permissionsService.GetPermisionByDepartmentType(DepartmentId, (PermissionTypes)type);
				var result = _permissionsService.SetPermissionForDepartment(DepartmentId, UserId, (PermissionTypes)type, (PermissionActions)perm, null, lockToGroup.GetValueOrDefault());

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.PermissionsChanged;
				auditEvent.Before = before.CloneJson();
				auditEvent.After = result.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return new StatusCodeResult((int)HttpStatusCode.OK);
			}

			return new StatusCodeResult((int)HttpStatusCode.NotModified);
		}

		public IActionResult SetPermissionData(int type, string data, bool? lockToGroup)
		{
			if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
			{
				var before = _permissionsService.GetPermisionByDepartmentType(DepartmentId, (PermissionTypes)type);
				var result = _permissionsService.SetPermissionForDepartment(DepartmentId, UserId, (PermissionTypes)type, (PermissionActions)before.Action, data, lockToGroup.GetValueOrDefault());

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.PermissionsChanged;
				auditEvent.Before = before.CloneJson();
				auditEvent.After = result.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return new StatusCodeResult((int)HttpStatusCode.OK);
			}

			return new StatusCodeResult((int)HttpStatusCode.NotModified);
		}

		public IActionResult GetRolesForPermission(int type)
		{
			var before = _permissionsService.GetPermisionByDepartmentType(DepartmentId, (PermissionTypes)type);

			if (before != null)
				return Json(before.Data);

			return Json("");
		}
		#endregion Async
	}
}
