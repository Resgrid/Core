using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using MongoDB.Driver;
using Resgrid.Model;
using Resgrid.Model.Providers;
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
		private readonly ICustomStateService _customStateService;
		private readonly ICertificationService _certificationService;
		private readonly IDocumentsService _documentsService;
		private readonly INotesService _notesService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IContactsService _contactsService;

		private static string WhoCanViewUnitsCacheKey = "ViewUnitsSecurityMaxtix_{0}";
		private static string WhoCanViewUnitLocationsCacheKey = "ViewUnitLocationsSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelCacheKey = "ViewUsersSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelLocationsCacheKey = "ViewUserLocationsSecurityMaxtix_{0}";

		public AuthorizationService(IDepartmentsService departmentsService, IInvitesService invitesService,
			ICallsService callsService, IMessageService messageService, IWorkLogsService workLogsService, ISubscriptionsService subscriptionsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IPermissionsService permissionsService, ICalendarService calendarService, IProtocolsService protocolsService,
			IShiftsService shiftsService, ICustomStateService customStateService, ICertificationService certificationService,
			IDocumentsService documentsService, INotesService notesService, ICacheProvider cacheProvider, IContactsService contactsService)
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
			_customStateService = customStateService;
			_certificationService = certificationService;
			_documentsService = documentsService;
			_notesService = notesService;
			_cacheProvider = cacheProvider;
			_contactsService = contactsService;
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

		public async Task<bool> CanUserViewUnitLocationAsync(string userId, int unitId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CanSeeUnitLocations);

			if (permission == null)
				return true;

			bool isGroupAdmin = false;
			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && department.IsUserAnAdmin(userId))
			{ // Department Admins only
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (not locked to group)
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (locked to group)
				if (department.IsUserAnAdmin(userId))
					return true; // Department Admins have access.

				if (unit.StationGroupId.HasValue && group != null &&
				    unit.StationGroupId.Value == group.DepartmentGroupId)
					return true; // Group admin in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && department.IsUserAnAdmin(userId))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !department.IsUserAnAdmin(userId))
			{
				if (permission.LockToGroup && unit.StationGroupId.HasValue && group != null &&
				    unit.StationGroupId.Value != group.DepartmentGroupId)
					return false;

				if (!String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
						where roleIds.Contains(r.PersonnelRoleId)
						select r;

					if (role.Any())
					{
						return true;
					}
				}

			}
			else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
			{
				if (unit.StationGroupId.HasValue && group != null &&
				    unit.StationGroupId.Value == group.DepartmentGroupId)
					return true; // Everyone in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup)
			{
				return true;
			}

			return false;
		}

		public async Task<bool> CanUserViewPersonLocationAsync(string userId, string targetUserId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CanSeePersonnelLocations);

			if (permission == null)
				return true;

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var targetUserGroup = await _departmentGroupsService.GetGroupForUserAsync(targetUserId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && department.IsUserAnAdmin(userId))
			{ // Department Admins only
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (not locked to group)
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (locked to group)
				if (department.IsUserAnAdmin(userId))
					return true; // Department Admins have access.

				if (group != null && targetUserGroup != null && group.DepartmentGroupId == targetUserGroup.DepartmentGroupId)
					return true; // Group admin in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && department.IsUserAnAdmin(userId))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !department.IsUserAnAdmin(userId))
			{
				if (permission.LockToGroup && group != null && targetUserGroup != null && group.DepartmentGroupId != targetUserGroup.DepartmentGroupId)
					return false;

				if (!String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
						where roleIds.Contains(r.PersonnelRoleId)
						select r;

					if (role.Any())
					{
						return true;
					}
				}

			}
			else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
			{
				if (group != null && targetUserGroup != null && group.DepartmentGroupId != targetUserGroup.DepartmentGroupId)
					return true; // Everyone in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup)
			{
				return true;
			}

			return false;
		}

		public async Task<bool> CanUserViewPersonAsync(string userId, string targetUserId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewGroupUsers);

			if (permission == null)
				return true;

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var targetUserGroup = await _departmentGroupsService.GetGroupForUserAsync(targetUserId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && department.IsUserAnAdmin(userId))
			{ // Department Admins only
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (not locked to group)
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (locked to group)
				if (department.IsUserAnAdmin(userId))
					return true; // Department Admins have access.

				if (group != null && targetUserGroup != null && group.DepartmentGroupId == targetUserGroup.DepartmentGroupId)
					return true; // Group admin in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && department.IsUserAnAdmin(userId))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !department.IsUserAnAdmin(userId))
			{
				if (permission.LockToGroup && group != null && targetUserGroup != null && group.DepartmentGroupId != targetUserGroup.DepartmentGroupId)
					return false;

				if (!String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
						where roleIds.Contains(r.PersonnelRoleId)
						select r;

					if (role.Any())
					{
						return true;
					}
				}

			}
			else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
			{
				if (group != null && targetUserGroup != null && group.DepartmentGroupId != targetUserGroup.DepartmentGroupId)
					return true; // Everyone in the same group have access to locked to group
			}
			else if (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup)
			{
				return true;
			}

			return false;
		}

		/// <summary>
		/// Purpose of this method is to determine if a user can view all people in a department. This is used for the personnel lists where we have an "All" option.
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="departmentId"></param>
		/// <returns></returns>
		public async Task<bool> CanUserViewAllPeopleAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewGroupUsers);

			if (permission == null)
				return true;

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);

			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && department.IsUserAnAdmin(userId))
			{ // Department Admins only
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (not locked to group)
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (locked to group)
				return true; // Department Admins have access.
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && department.IsUserAnAdmin(userId))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !department.IsUserAnAdmin(userId))
			{
				if (permission.LockToGroup)
					return false;

				if (!String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						return true;
					}
				}

			}
			else if (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup)
			{
				return true;
			}

			return false;
		}

		public async Task<bool> CanUserDeleteCallAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.DeleteCall);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (!department.IsUserInDepartment(userId))
				return false;

			bool isGroupAdmin = false;
			bool isUserGroupInDispatch = false;
			int userGroupId = 0;
			int callGroupId = -1;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

			if (group != null)
			{
				isGroupAdmin = group.IsUserGroupAdmin(userId);
				userGroupId = group.DepartmentGroupId;
			}

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId)
				return false;

			call = await _callsService.PopulateCallData(call, false, false, false, true, false, false, false, false, false);

			if (group != null)
			{
				isUserGroupInDispatch = call.HasGroupBeenDispatched(group.DepartmentGroupId);

				if (isUserGroupInDispatch)
					callGroupId = userGroupId;
			}

			return _permissionsService.IsUserAllowed(permission, departmentId, callGroupId, userGroupId, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserCloseCallAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CloseCall);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (!department.IsUserInDepartment(userId))
				return false;

			bool isGroupAdmin = false;
			bool isUserGroupInDispatch = false;
			int userGroupId = 0;
			int callGroupId = -1;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

			if (group != null)
			{
				isGroupAdmin = group.IsUserGroupAdmin(userId);
				userGroupId = group.DepartmentGroupId;
			}

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId)
				return false;

			call = await _callsService.PopulateCallData(call, false, false, false, true, false, false, false, false, false);

			if (group != null)
			{
				isUserGroupInDispatch = call.HasGroupBeenDispatched(group.DepartmentGroupId);

				if (isUserGroupInDispatch)
					callGroupId = userGroupId;
			}

			return _permissionsService.IsUserAllowed(permission, departmentId, callGroupId, userGroupId, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserAddCallDataAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AddCallData);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (!department.IsUserInDepartment(userId))
				return false;

			bool isGroupAdmin = false;
			bool isUserGroupInDispatch = false;
			int userGroupId = 0;
			int callGroupId = -1;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

			if (group != null)
			{
				isGroupAdmin = group.IsUserGroupAdmin(userId);
				userGroupId = group.DepartmentGroupId;
			}

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId)
				return false;

			call = await _callsService.PopulateCallData(call, false, false, false, true, false, false, false, false, false);

			if (group != null)
			{
				isUserGroupInDispatch = call.HasGroupBeenDispatched(group.DepartmentGroupId);

				if (isUserGroupInDispatch)
					callGroupId = userGroupId;
			}

			return _permissionsService.IsUserAllowed(permission, departmentId, callGroupId, userGroupId, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserDeleteDepartmentAsync(string userId, int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return false;

			if (department.ManagingUserId != userId)
				return false;

			return true;
		}

		public async Task<bool> CanUserModifyCustomStatusAsync(string userId, int customStatusId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var customState = await _customStateService.GetCustomSateByIdAsync(customStatusId);

			if (department == null || customState == null)
				return false;

			if (customState.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserModifyCustomStateDetailAsync(string userId, int customStateDetailId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var customStateDetail = await _customStateService.GetCustomDetailByIdAsync(customStateDetailId);

			if (department == null || customStateDetail == null)
				return false;

			var customState = await _customStateService.GetCustomSateByIdAsync(customStateDetail.CustomStateId);

			if (customState == null)
				return false;

			if (customState.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserModifyCallTypeAsync(string userId, int callTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var callType = await _callsService.GetCallTypeByIdAsync(callTypeId);

			if (department == null || callType == null)
				return false;

			if (callType.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddCallTypeAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddCallPriorityAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteCallPriorityAsync(string userId, int priorityId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var priority = await _callsService.GetCallPrioritiesByIdAsync(department.DepartmentId, priorityId, true);

			if (priority == null)
				return false;

			if (priority.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserEditCallPriorityAsync(string userId, int priorityId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var priority = await _callsService.GetCallPrioritiesByIdAsync(department.DepartmentId, priorityId, true);

			if (priority == null)
				return false;

			if (priority.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddUnitTypeAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserEditUnitTypeAsync(string userId, int unitTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var unitType = await _unitsService.GetUnitTypeByIdAsync(unitTypeId);

			if (unitType == null)
				return false;

			if (unitType.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddCertificationTypeAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteCertificationTypeAsync(string userId, int certificationTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _certificationService.GetCertificationTypeByIdAsync(certificationTypeId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddDocumentTypeAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteDocumentTypeAsync(string userId, string documentTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _documentsService.GetDocumentCategoryByIdAsync(documentTypeId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddNoteTypeAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteNoteTypeAsync(string userId, string noteTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _notesService.GetNoteCategoryByIdAsync(noteTypeId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserAddNoteAsync(string userId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserEditNoteAsync(string userId, int noteId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _notesService.GetNoteByIdAsync(noteId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewPersonViaMatrixAsync(string userToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelCacheKey, departmentId));

			// Fail open if the cache is not available for now. -SJ 8-26-2024
			if (matrix == null)
				return true;

			if (matrix.EveryoneNoGroupLock)
				return true;

			if (userToView == userId)
				return true;

			if (!matrix.Users.ContainsKey(userToView))
				return true;

			var userViewList = matrix.Users[userToView];

			if (userViewList.Contains(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewPersonLocationViaMatrixAsync(string userToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelLocationsCacheKey, departmentId));

			// Fail open if the cache is not available for now. -SJ 8-26-2024
			if (matrix == null)
				return true;

			if (matrix.EveryoneNoGroupLock)
				return true;

			if (userToView == userId)
				return true;

			if (!matrix.Users.ContainsKey(userToView))
				return true;

			var userViewList = matrix.Users[userToView];

			if (userViewList.Contains(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewUnitViaMatrixAsync(int unitToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUnits>(string.Format(WhoCanViewUnitsCacheKey, departmentId));

			// Fail open if the cache is not available for now. -SJ 8-26-2024
			if (matrix == null)
				return true;

			if (matrix.EveryoneNoGroupLock)
				return true;

			if (!matrix.Units.ContainsKey(unitToView))
				return true;

			var userViewList = matrix.Units[unitToView];

			if (userViewList.Contains(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserViewUnitLocationViaMatrixAsync(int unitToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUnits>(string.Format(WhoCanViewUnitLocationsCacheKey, departmentId));

			// Fail open if the cache is not available for now. -SJ 8-26-2024
			if (matrix == null)
				return true;

			if (matrix.EveryoneNoGroupLock)
				return true;

			if (!matrix.Units.ContainsKey(unitToView))
				return true;

			var userViewList = matrix.Units[unitToView];

			if (userViewList.Contains(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteContactNoteTypeAsync(string userId, string contactNoteTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _contactsService.GetContactNoteTypeByIdAsync(contactNoteTypeId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserEditContactNoteTypeAsync(string userId, string contactNoteTypeId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);

			if (department == null)
				return false;

			var type = await _contactsService.GetContactNoteTypeByIdAsync(contactNoteTypeId);

			if (type == null)
				return false;

			if (type.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteContactAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ContactDelete);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserAddOrEditContactAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ContactEdit);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

	}
}
