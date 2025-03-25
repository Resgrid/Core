using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Notifications;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class NotificationsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly INotificationService _notificationService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;

		public NotificationsController(INotificationService notificationService, IPersonnelRolesService personnelRolesService, IDepartmentGroupsService departmentGroupsService,
			IUsersService usersService, IUnitsService unitsService, ICustomStateService customStateService)
		{
			_notificationService = notificationService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_usersService = usersService;
			_unitsService = unitsService;
			_customStateService = customStateService;
		}
		#endregion Private Members and Constructors

		public async Task<IActionResult> Index()
		{
			var model = new NotificationIndexView();
			model.Notifications =  await _notificationService.GetNotificationsByDepartmentAsync(DepartmentId);
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			model.CustomStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId);

			foreach (var notification in model.Notifications)
			{
				if (notification.Everyone)
					model.NotifyUsers.Add(notification.DepartmentNotificationId, "Everyone");
				else if (notification.DepartmentAdmins)
					model.NotifyUsers.Add(notification.DepartmentNotificationId, "Department Admins");
				else if (notification.SelectedGroupsAdminsOnly)
					model.NotifyUsers.Add(notification.DepartmentNotificationId, "Selected Group(s) Admins");
				else
				{
					var sb = new StringBuilder();

					if (!String.IsNullOrWhiteSpace(notification.RolesToNotify))
					{
						var roles = notification.RolesToNotify.Split(char.Parse(","));
						foreach (var roleId in roles)
						{
							var role = await _personnelRolesService.GetRoleByIdAsync(int.Parse(roleId));

							if (role != null)
							{
								if (sb.Length > 0)
									sb.Append("," + role.Name);
								else
									sb.Append(role.Name);
							}
						}
					}

					if (!String.IsNullOrWhiteSpace(notification.GroupsToNotify))
					{
						var groups = notification.GroupsToNotify.Split(char.Parse(","));
						foreach (var groupId in groups)
						{
							var group = await _departmentGroupsService.GetGroupByIdAsync(int.Parse(groupId), false);

							if (group != null)
							{
								if (sb.Length > 0)
									sb.Append("," + group.Name);
								else
									sb.Append(group.Name);
							}
						}
					}

					if (!String.IsNullOrWhiteSpace(notification.UsersToNotify))
					{
						var users = notification.UsersToNotify.Split(char.Parse(","));
						foreach (var userId in users)
						{
							var user = _usersService.GetUserById(userId);

							if (sb.Length > 0)
								sb.Append("," + UserHelper.GetFullNameForUser(user.UserId));
							else
								sb.Append(UserHelper.GetFullNameForUser(user.UserId));
						}
					}

					model.NotifyUsers.Add(notification.DepartmentNotificationId, sb.ToString());
				}

				if (notification.EventType == (int)EventTypes.RolesInGroupAvailabilityAlert || notification.EventType == (int)EventTypes.RolesInDepartmentAvailabilityAlert)
				{
					if (!String.IsNullOrWhiteSpace(notification.Data) && allRoles.Any(x => x.PersonnelRoleId == int.Parse(notification.Data)))
						model.NotifyData.Add(notification.DepartmentNotificationId, allRoles.First(x => x.PersonnelRoleId == int.Parse(notification.Data)).Name);
				}
				else if (notification.EventType == (int)EventTypes.UnitTypesInGroupAvailabilityAlert || notification.EventType == (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert)
				{
					if (!String.IsNullOrWhiteSpace(notification.Data) && unitTypes.Any(x => x.UnitTypeId == int.Parse(notification.Data)))
						model.NotifyData.Add(notification.DepartmentNotificationId, unitTypes.First(x => x.UnitTypeId == int.Parse(notification.Data)).Type);
				}
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			var model = new NotificationNewView();
			model.Notification = new DepartmentNotification();

			ViewBag.Types = model.Type.ToSelectList();

			model.PersonnelRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			model.UnitsTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> New(NotificationNewView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			ViewBag.Types = model.Type.ToSelectList();
			model.Notification.DepartmentId = DepartmentId;
			model.Notification.EventType = (int)model.Type;

			var roles = new List<string>();
			var groups = new List<string>();
			var users = new List<string>();
			int lowerLimit = 0;
			int upperLimit = 0;

			if (collection.ContainsKey("roles") != null)
				roles.AddRange(collection["roles"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("groups") != null)
				groups.AddRange(collection["groups"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("users") != null)
				users.AddRange(collection["users"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("lowerLimit") != null)
				int.TryParse(collection["lowerLimit"], out lowerLimit);

			if (collection.ContainsKey("upperLimit") != null)
				int.TryParse(collection["upperLimit"], out upperLimit);

			model.Notification.LowerLimit = lowerLimit;
			model.Notification.UpperLimit = upperLimit;

			if (ModelState.IsValid)
			{
				foreach (var user in users)
				{
					if (!String.IsNullOrWhiteSpace(user))
					{
						model.Notification.AddUserToNotify(user);
					}
				}

				foreach (var group in groups)
				{
					if (!String.IsNullOrWhiteSpace(group))
					{
						model.Notification.AddGroupToNotify(int.Parse(group));
					}
				}

				foreach (var role in roles)
				{
					if (!String.IsNullOrWhiteSpace(role))
					{
						model.Notification.AddRoleToNotify(int.Parse(role));
					}
				}

				if (model.Type == EventTypes.RolesInGroupAvailabilityAlert)
				{
					model.Notification.Data = model.SelectedRole.ToString();

					if (collection.ContainsKey("currentStates") != null)
						model.Notification.CurrentData = collection["currentStates"];
				}
				else if (model.Type == EventTypes.UnitTypesInGroupAvailabilityAlert)
				{
					model.Notification.Data = model.SelectedUnitType.ToString();

					if (collection.ContainsKey("currentStates") != null)
						model.Notification.CurrentData = collection["currentStates"];
				}
				else if (model.Type == EventTypes.RolesInDepartmentAvailabilityAlert)
				{
					model.Notification.LockToGroup = false;
					model.Notification.Data = model.SelectedRole.ToString();

					if (collection.ContainsKey("currentStates") != null)
						model.Notification.CurrentData = collection["currentStates"];
				}
				else if (model.Type == EventTypes.UnitTypesInDepartmentAvailabilityAlert)
				{
					model.Notification.LockToGroup = false;
					model.Notification.Data = model.SelectedUnitType.ToString();

					if (collection.ContainsKey("currentStates") != null)
						model.Notification.CurrentData = collection["currentStates"];
				}

				await _notificationService.SaveAsync(model.Notification, cancellationToken);

				return RedirectToAction("Index");
			}

			model.PersonnelRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			model.UnitsTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Delete(int notificationId, CancellationToken cancellationToken)
		{
			await _notificationService.DeleteDepartmentNotificationByIdAsync(notificationId, cancellationToken);

			return RedirectToAction("Index");
		}
	}
}
