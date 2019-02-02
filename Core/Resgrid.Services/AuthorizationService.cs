using System;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AuthorizationService : IAuthorizationService
	{
		#region Private Members and Constructors
		private readonly IPushUriService _pushUriService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IInvitesService _invitesService;
		private readonly ICallsService _callsService;
		private readonly IMessageService _messageService;
		private readonly IWorkLogsService _workLogsService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly IPermissionsService _permissionsService;
		private readonly ICalendarService _calendarService;

		public AuthorizationService(IPushUriService pushUriService, IDepartmentsService departmentsService, IInvitesService invitesService,
			ICallsService callsService, IMessageService messageService, IWorkLogsService workLogsService, ISubscriptionsService subscriptionsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IPermissionsService permissionsService, ICalendarService calendarService)
		{
			_pushUriService = pushUriService;
			_departmentsService = departmentsService;
			_invitesService = invitesService;
			_callsService = callsService;
			_messageService = messageService;
			_workLogsService = workLogsService;
			_subscriptionsService = subscriptionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_permissionsService = permissionsService;
			_calendarService = calendarService;
		}
		#endregion Private Members and Constructors

		public bool CanUserDeletePushUri(string userId, int pushUriId)
		{
			var pushUri = _pushUriService.GetPushUriById(pushUriId);

			if (pushUri != null)
			{
				if (pushUri.UserId == userId)
					return true;
			}

			return false;
		}

		public bool CanUserManageInvite(string userId, int inviteId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var invite = _invitesService.GetInviteById(inviteId);

			if (department == null || invite == null)
				return false;

			if (!department.IsUserAnAdmin(userId))
				return false;

			if (invite.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public bool CanUserViewCall(string userId, int callId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var call = _callsService.GetCallById(callId, false);

			if (department == null || call == null)
				return false;

			if (call.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public bool CanUserEditCall(string userId, int callId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var call = _callsService.GetCallById(callId, false);

			if (department == null || call == null)
				return false;

			if (call.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (call.ReportingUserId != userId)
				return false;

			return true;
		}

		public bool CanUserViewMessage(string userId, int messageId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var message = _messageService.GetMessageById(messageId);

			if (department == null || message == null)
				return false;

			return CanUserViewMessage(userId, message);
		}

		public bool CanUserViewMessage(string userId, Message message)
		{
			if (message == null)
				return false;

			if ((message.ReceivingUserId == userId || message.SendingUserId == userId))
				return true;

			if (message.MessageRecipients.Any() && message.MessageRecipients.Select(x => x.UserId).Contains(userId))
				return true;

			return false;
		}

		public bool CanUserViewAndEditCallLog(string userId, int callLogId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var log = _workLogsService.GetCallLogById(callLogId);

			if (department == null || log == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (log.LoggedByUserId != userId)
				return false;

			return true;
		}

		public bool CanUserViewAndEditWorkLog(string userId, int logId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var log = _workLogsService.GetWorkLogById(logId);

			if (department == null || log == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (log.LoggedByUserId != userId)
				return false;

			return true;
		}

		public bool CanUserViewPayment(string userId, int paymentId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var payment = _subscriptionsService.GetPaymentById(paymentId);

			if (department == null || payment == null)
				return false;

			if (payment.DepartmentId != department.DepartmentId)
				return false;

			if (!department.IsUserAnAdmin(userId))
				return false;

			return true;
		}

		public bool CanUserEditDepartmentGroup(string userId, int departmentGroupId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var group = _departmentGroupsService.GetGroupById(departmentGroupId);

			if (department == null || group == null)
				return false;

			if (group.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (!group.IsUserGroupAdmin(userId))
				return false;

			return true;
		}

		public bool CanUserEditRole(string userId, int personnelRoleId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var role = _personnelRolesService.GetRoleById(personnelRoleId);

			if (department == null || role == null)
				return false;

			if (role.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public bool CanUserViewRole(string userId, int personnelRoleId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var role = _personnelRolesService.GetRoleById(personnelRoleId);

			if (department == null || role == null)
				return false;

			if (role.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		/// <summary>
		/// Determines if a user is valid within the limits of the current departments plan and that
		/// standard conditions are met (i.e. the user is not disabled)
		/// </summary>
		/// <param name="userId">UserId to check</param>
		/// <returns>True if the user is in a valid state, otherwise false</returns>
		public bool IsUserValidWithinLimits(string userId, int departmentId)
		{
			if (_departmentsService.IsUserDisabled(userId, departmentId))
				return false;

			var department = _departmentsService.GetDepartmentById(departmentId);

			if (department == null)
				return false;

			var users = _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabled(department.DepartmentId);

			// This was .All and was failing with an array of 1, but seemed to work other times.
			if (!users.Any(x => x.Id == userId))
				return false;

			return true;
		}

		public bool CanUserModifyUnit(string userId, int unitId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var unit = _unitsService.GetUnitById(unitId);

			if (department == null || unit == null)
				return false;

			if (unit.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public bool CanUserViewUnit(string userId, int unitId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var unit = _unitsService.GetUnitById(unitId);

			if (department == null || unit == null)
				return false;

			if (unit.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public bool CanUserViewUser(string viewerUserId, string targetUserId)
		{
			var department = _departmentsService.GetDepartmentByUserId(viewerUserId);
			var department1 = _departmentsService.GetDepartmentByUserId(targetUserId);

			if (department.DepartmentId != department1.DepartmentId)
				return false;

			return true;
		}

		public bool CanGroupAdminsAddUsers(int departmentId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.AddPersonnel);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins)
				return true;

			return false;
		}

		public bool CanGroupAdminsRemoveUsers(int departmentId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.RemovePersonnel);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins)
				return true;

			return false;
		}

		public bool CanUserAddNewUser(int departmentId, string userId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.AddPersonnel);
			var isGroupAdmin = _departmentGroupsService.IsUserAGroupAdmin(userId, departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isGroupAdmin)
				return true;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public bool CanUserDeleteUser(int departmentId, string userId, string userIdToDelete)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.RemovePersonnel);
			var adminGroup = _departmentGroupsService.GetGroupForUser(userId, departmentId);
			var destGroup = _departmentGroupsService.GetGroupForUser(userIdToDelete, departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);

			if (department.IsUserAnAdmin(userId))
				return true;

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (adminGroup.IsUserGroupAdmin(userId) && destGroup.DepartmentGroupId == adminGroup.DepartmentGroupId))
				return true;

			return false;
		}

		public bool CanUserCreateCall(string userId, int departmentId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.CreateCall);

			bool isGroupAdmin = false;
			var group = _departmentGroupsService.GetGroupForUser(userId, departmentId);
			var roles = _personnelRolesService.GetRolesForUser(userId, departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public bool CanUserViewPII(string userId, int departmentId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.ViewPersonalInfo);

			bool isGroupAdmin = false;
			var group = _departmentGroupsService.GetGroupForUser(userId, departmentId);
			var roles = _personnelRolesService.GetRolesForUser(userId, departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public bool CanUserCreateNote(string userId, int departmentId)
		{
			var permission = _permissionsService.GetPermisionByDepartmentType(departmentId, PermissionTypes.CreateNote);

			bool isGroupAdmin = false;
			var group = _departmentGroupsService.GetGroupForUser(userId, departmentId);
			var roles = _personnelRolesService.GetRolesForUser(userId, departmentId);
			var department = _departmentsService.GetDepartmentById(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public bool CanUserModifyCalendarEntry(string userId, int calendarItemId)
		{
			var department = _departmentsService.GetDepartmentByUserId(userId);
			var item = _calendarService.GetCalendarItemById(calendarItemId);

			if (department == null || item == null)
				return false;

			if (item.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (item.CreatorUserId == userId)
				return true;

			if (item.CreatorUserId == userId)
				return true;

			return false;
		}
	}
}
