using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AuthorizationService : IAuthorizationService
	{
		#region Private Members and Constructors
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
		private readonly IProtocolsService _protocolsService;
		private readonly IShiftsService _shiftsService;

		public AuthorizationService(IDepartmentsService departmentsService, IInvitesService invitesService,
			ICallsService callsService, IMessageService messageService, IWorkLogsService workLogsService, ISubscriptionsService subscriptionsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IPermissionsService permissionsService, ICalendarService calendarService, IProtocolsService protocolsService,
			IShiftsService shiftsService)
		{
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
			_protocolsService = protocolsService;
			_shiftsService = shiftsService;
		}
		#endregion Private Members and Constructors

		public async Task<bool> CanUserManageInviteAsync(string userId, int inviteId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var invite = await _invitesService.GetInviteByIdAsync(inviteId);

			if (department == null || invite == null)
				return false;

			if (!department.IsUserAnAdmin(userId))
				return false;

			if (invite.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public async Task<bool> CanUserViewCallAsync(string userId, int callId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var call = await _callsService.GetCallByIdAsync(callId, false);

			if (department == null || call == null)
				return false;

			if (call.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public async Task<bool> CanUserEditCallAsync(string userId, int callId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var call = await _callsService.GetCallByIdAsync(callId, false);

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

		public async Task<bool> CanUserViewMessageAsync(string userId, int messageId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var message = await _messageService.GetMessageByIdAsync(messageId);

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

		public async Task<bool> CanUserViewAndEditCallLogAsync(string userId, int callLogId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var log = await _workLogsService.GetCallLogByIdAsync(callLogId);

			if (department == null || log == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			if (log.LoggedByUserId != userId)
				return false;

			return true;
		}

		public async Task<bool> CanUserViewAndEditWorkLogAsync(string userId, int logId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var log = await _workLogsService.GetWorkLogByIdAsync(logId);

			if (department == null || log == null)
				return false;

			var logDepartment = await _departmentsService.GetDepartmentByIdAsync(log.DepartmentId);

			if (department.DepartmentId != logDepartment.DepartmentId)
				return false;

			if (logDepartment.IsUserAnAdmin(userId))
				return true;

			if (log.LoggedByUserId == userId)
				return true;

			if (log.Users.Any(x => x.UserId == userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteWorkLogAsync(string userId, int logId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var log = await _workLogsService.GetWorkLogByIdAsync(logId);

			if (department == null || log == null)
				return false;

			var logDepartment = await _departmentsService.GetDepartmentByIdAsync(log.DepartmentId);

			if (department.DepartmentId != logDepartment.DepartmentId)
				return false;

			if (logDepartment.IsUserAnAdmin(userId))
				return true;

			if (log.LoggedByUserId == userId)
				return true;

			return false;
		}

		public async Task<bool> CanUserViewPaymentAsync(string userId, int paymentId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var payment = await _subscriptionsService.GetPaymentByIdAsync(paymentId);

			if (department == null || payment == null)
				return false;

			if (payment.DepartmentId != department.DepartmentId)
				return false;

			if (!department.IsUserAnAdmin(userId))
				return false;

			return true;
		}

		public async Task<bool> CanUserEditDepartmentGroupAsync(string userId, int departmentGroupId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var group = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId);

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

		public async Task<bool> CanUserEditRoleAsync(string userId, int personnelRoleId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var role = await _personnelRolesService.GetRoleByIdAsync(personnelRoleId);

			if (department == null || role == null)
				return false;

			if (role.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewRoleAsync(string userId, int personnelRoleId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var role = await _personnelRolesService.GetRoleByIdAsync(personnelRoleId);

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
		public async Task<bool> IsUserValidWithinLimitsAsync(string userId, int departmentId)
		{
			if (await _departmentsService.IsUserDisabledAsync(userId, departmentId))
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return false;

			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(department.DepartmentId);

			// This was .All and was failing with an array of 1, but seemed to work other times.
			if (!users.Any(x => x.Id == userId))
				return false;

			return true;
		}

		public async Task<bool> CanUserModifyUnitAsync(string userId, int unitId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (department == null || unit == null)
				return false;

			if (unit.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewUnitAsync(string userId, int unitId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (department == null || unit == null)
				return false;

			if (unit.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public async Task<bool> CanUserViewUserAsync(string viewerUserId, string targetUserId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(viewerUserId);
			var department1 = await _departmentsService.GetDepartmentByUserIdAsync(targetUserId);

			if (department.DepartmentId != department1.DepartmentId)
				return false;

			return true;
		}

		public async Task<bool> CanGroupAdminsAddUsersAsync(int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AddPersonnel);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins)
				return true;

			return false;
		}

		public async Task<bool> CanGroupAdminsRemoveUsersAsync(int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.RemovePersonnel);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins)
				return true;

			return false;
		}

		public async Task<bool> CanUserAddNewUserAsync(int departmentId, string userId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AddPersonnel);
			var isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isGroupAdmin)
				return true;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteUserAsync(int departmentId, string userId, string userIdToDelete)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.RemovePersonnel);
			var adminGroup = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var destGroup = await _departmentGroupsService.GetGroupForUserAsync(userIdToDelete, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department.IsUserAnAdmin(userId))
				return true;

			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (adminGroup.IsUserGroupAdmin(userId) && destGroup.DepartmentGroupId == adminGroup.DepartmentGroupId))
				return true;

			return false;
		}

		public async Task<bool> CanUserCreateCallAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateCall);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserViewPIIAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewPersonalInfo);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserCreateNoteAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateNote);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserModifyCalendarEntryAsync(string userId, int calendarItemId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var item = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

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

		public async Task<bool> CanUserEditProfileAsync(string userId, int departmentId, string editingProfileId)
		{
			if (userId == editingProfileId)
				return true;

			var usersDepartments = await _departmentsService.GetAllDepartmentsForUserAsync(editingProfileId);

			if (usersDepartments == null || !usersDepartments.Any())
				return false;

			var hasDepartmentIdMatch = usersDepartments.Any(x => x.DepartmentId == departmentId);

			if (!hasDepartmentIdMatch)
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department.IsUserAnAdmin(userId))
				return true;

			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			if (group != null)
			{
				if (group.IsUserGroupAdmin(userId) && group.IsUserInGroup(editingProfileId))
					return true;
			}


			return false;
		}

		public async Task<bool> CanUserModifyProtocolAsync(string userId, int protocolId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);

			if (department == null || protocol == null)
				return false;

			if (protocol.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewProtocolAsync(string userId, int protocolId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);

			if (department == null || protocol == null)
				return false;

			if (protocol.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

		public async Task<bool> CanUserManageSubscriptionAsync(string userId, int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, true);

			if (department == null)
				return false;

			if (!department.IsUserAnAdmin(userId))
				return false;

			return true;
		}

		public async Task<bool> CanUserDeleteShiftSignupAsync(string userId, int departmentId, int shiftSignupId)
		{
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);
			var usersDepartments = await _departmentsService.GetAllDepartmentsForUserAsync(userId);

			if (usersDepartments == null || !usersDepartments.Any())
				return false;

			var hasDepartmentIdMatch = usersDepartments.Any(x => x.DepartmentId == departmentId);

			if (!hasDepartmentIdMatch)
				return false;

			if (signup == null)
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department.IsUserAnAdmin(userId))
				return true;

			if (signup.DepartmentGroupId.HasValue)
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(signup.DepartmentGroupId.Value);
				if (group != null)
				{
					if (group.IsUserGroupAdmin(userId))
						return true;
				}
			}

			if (signup.UserId == userId)
				return true;

			return false;
		}
	}
}
