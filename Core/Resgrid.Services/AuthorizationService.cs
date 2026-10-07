using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using MongoDB.Driver;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Events;
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
		private readonly IEventAggregator _eventAggregator;
		private readonly IDispatchScopeService _dispatchScopeService;

		private static string WhoCanViewUnitsCacheKey = "ViewUnitsSecurityMaxtix_{0}";
		private static string WhoCanViewUnitLocationsCacheKey = "ViewUnitLocationsSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelCacheKey = "ViewUsersSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelLocationsCacheKey = "ViewUserLocationsSecurityMaxtix_{0}";

		/// <summary>
		/// Suppression window for the self-heal refresh below, so one stale matrix does not enqueue a
		/// rebuild per entity per request.
		/// </summary>
		private static readonly TimeSpan StaleMatrixRefreshDebounce = TimeSpan.FromMinutes(2);
		private static readonly ConcurrentDictionary<string, DateTime> LastStaleMatrixRefresh = new ConcurrentDictionary<string, DateTime>();

		public AuthorizationService(IDepartmentsService departmentsService, IInvitesService invitesService,
			ICallsService callsService, IMessageService messageService, IWorkLogsService workLogsService, ISubscriptionsService subscriptionsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IPermissionsService permissionsService, ICalendarService calendarService, IProtocolsService protocolsService,
			IShiftsService shiftsService, ICustomStateService customStateService, ICertificationService certificationService,
			IDocumentsService documentsService, INotesService notesService, ICacheProvider cacheProvider, IContactsService contactsService,
			IEventAggregator eventAggregator, IDispatchScopeService dispatchScopeService)
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
			_eventAggregator = eventAggregator;
			_dispatchScopeService = dispatchScopeService;
		}

		/// <summary>
		/// The visibility matrix is a snapshot: entities created after it was built are simply absent,
		/// and so is the whole matrix when the cache is off or has dropped it. Ask for a rebuild and answer
		/// this one request from the permission rows (the live helpers below); the next request reads a
		/// correct matrix.
		/// </summary>
		private void RequestMatrixRefresh(int departmentId, SecurityCacheTypes type)
		{
			var key = $"{departmentId}_{(int)type}";
			var now = DateTime.UtcNow;

			var shouldSend = false;
			LastStaleMatrixRefresh.AddOrUpdate(key, _ =>
			{
				shouldSend = true;
				return now;
			}, (_, last) =>
			{
				if (now - last < StaleMatrixRefreshDebounce)
					return last;

				shouldSend = true;
				return now;
			});

			if (!shouldSend)
				return;

			_eventAggregator?.SendMessage<SecurityRefreshEvent>(new SecurityRefreshEvent
			{
				DepartmentId = departmentId,
				Type = type
			});
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

			// Group-scoped dispatch (off by default): outside the user's area and not on the call means no access.
			if (!await _dispatchScopeService.CanUserAccessCallAsync(department.DepartmentId, userId, call))
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

			if (logDepartment == null || department.DepartmentId != logDepartment.DepartmentId)
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

			if (logDepartment == null || department.DepartmentId != logDepartment.DepartmentId)
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

			// A deleted unit is kept only for point-in-time data: nothing edits, re-deletes or sets a status on it.
			if (department == null || unit == null || unit.IsDeleted)
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

			// Security > View Units: the same answer the unit lists get from the visibility matrix. No row means everyone.
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(department.DepartmentId, PermissionTypes.ViewGroupUnits);

			if (permission == null)
				return true;

			return await IsUserAllowedForUnitAsync(permission, userId, department.DepartmentId, unit);
		}

		public async Task<bool> CanUserViewUserAsync(string viewerUserId, string targetUserId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(viewerUserId);
			var department1 = await _departmentsService.GetDepartmentByUserIdAsync(targetUserId);

			// If we resolved both departments via member records, do a direct comparison
			if (department != null && department1 != null)
			{
				return department.DepartmentId == department1.DepartmentId;
			}

			// Fall back: one or both users may be a department managing user without an active
			// DepartmentMember record. Resolve the department from whichever side returned a result
			// and verify the other user belongs to that same department.
			int? resolvedDepartmentId = department?.DepartmentId ?? department1?.DepartmentId;

			if (resolvedDepartmentId == null)
				return false;

			var resolvedDepartment = await _departmentsService.GetDepartmentByIdAsync(resolvedDepartmentId.Value);

			if (resolvedDepartment == null)
				return false;

			// The managing user always belongs to their own department; confirm the other user is also a member
			if (resolvedDepartment.ManagingUserId == viewerUserId || resolvedDepartment.ManagingUserId == targetUserId)
			{
				string otherUserId = resolvedDepartment.ManagingUserId == viewerUserId ? targetUserId : viewerUserId;
				return await _departmentsService.IsMemberOfDepartmentAsync(resolvedDepartmentId.Value, otherUserId)
				       || resolvedDepartment.ManagingUserId == otherUserId;
			}

			return false;
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

			if (department != null && department.IsUserAnAdmin(userId))
				return true;

			return false;
		}

		public async Task<bool> CanUserDeleteUserAsync(int departmentId, string userId, string userIdToDelete)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.RemovePersonnel);
			var adminGroup = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var destGroup = await _departmentGroupsService.GetGroupForUserAsync(userIdToDelete, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			// A group admin never removes a department admin or the managing user, even one in their own group: the
			// removal can deactivate the whole account, and only a department admin outranks another.
			if (department.IsUserAnAdmin(userIdToDelete))
				return false;

			// Either member can be in no group: then there is no shared group for a group admin to act on.
			if (permission != null && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && adminGroup != null && destGroup != null
			    && adminGroup.IsUserGroupAdmin(userId) && destGroup.DepartmentGroupId == adminGroup.DepartmentGroupId)
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

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
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

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
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

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
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
			if (department != null && department.IsUserAnAdmin(userId))
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
			if (department != null && department.IsUserAnAdmin(userId))
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

		public async Task<ShiftManagementScope> GetShiftManagementScopeAsync(string userId, int departmentId)
		{
			var scope = new ShiftManagementScope();

			if (String.IsNullOrWhiteSpace(userId))
				return scope;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return scope;

			// Before the member check: the managing user can be an admin without an active DepartmentMember record.
			if (department.IsUserAnAdmin(userId))
			{
				scope.AllGroups = true;
				return scope;
			}

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);

			if (member == null || member.IsDeleted)
				return scope;

			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateShift);

			if (permission != null)
			{
				if (permission.Action == (int)PermissionActions.Everyone)
				{
					scope.AllGroups = true;
					return scope;
				}

				if ((permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles ||
				     permission.Action == (int)PermissionActions.DepartmentAndGroupAdminsAndSelectRoles) &&
				    !String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(',')
						.Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null)
						.Where(x => x.HasValue)
						.Select(x => x.Value)
						.ToList();

					var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

					if (roles != null && roles.Any(x => roleIds.Contains(x.PersonnelRoleId)))
					{
						scope.AllGroups = true;
						return scope;
					}
				}
			}

			// Group admins supervise their own teams (and any child groups) whatever the Create Shift permission says,
			// the same way they could already remove signups in their group. This is how a contracted provider manages
			// only its own groups while the department sees everything.
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new System.Collections.Generic.List<DepartmentGroup>();

			foreach (var group in groups.Where(x => x != null && x.IsUserGroupAdmin(userId)))
				scope.GroupIds.UnionWith(DepartmentGroupHierarchy.GetSelfAndDescendantIds(groups, group.DepartmentGroupId));

			return scope;
		}

		public async Task<bool> CanUserViewUnitLocationAsync(string userId, int unitId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CanSeeUnitLocations);

			if (permission == null)
				return true;

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			return await IsUserAllowedForUnitAsync(permission, userId, departmentId, unit);
		}

		/// <summary>
		/// One unit permission (View Units or See Unit Locations) for one user, decided the way the unit visibility
		/// matrices decide it: locked to group means the unit's own station, except that admins of that station or of
		/// any group above it count for the locked "department and group admins" rule. Department admins always pass.
		/// </summary>
		private async Task<bool> IsUserAllowedForUnitAsync(Permission permission, string userId, int departmentId, Unit unit)
		{
			if (unit == null)
				return false;

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return false;

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			var departmentAdmin = department.IsUserAnAdmin(userId);
			var targetGroupId = unit.StationGroupId;
			var ancestorAdmin = !departmentAdmin && isGroupAdmin && permission.LockToGroup &&
				permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins &&
				await IsAdminOfGroupOrAncestorAsync(userId, targetGroupId);
			return ResourceVisibilityPermission.Allows(permission, departmentAdmin, isGroupAdmin,
				group?.DepartmentGroupId, targetGroupId, roles?.Select(r => r.PersonnelRoleId), ancestorAdmin);
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

			if (department == null)
				return false;

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			var departmentAdmin = department.IsUserAnAdmin(userId);
			var targetGroupId = targetUserGroup?.DepartmentGroupId;
			var ancestorAdmin = !departmentAdmin && isGroupAdmin && permission.LockToGroup &&
				permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins &&
				await IsAdminOfGroupOrAncestorAsync(userId, targetGroupId);
			return ResourceVisibilityPermission.Allows(permission, departmentAdmin, isGroupAdmin,
				group?.DepartmentGroupId, targetGroupId, roles?.Select(r => r.PersonnelRoleId), ancestorAdmin);
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

			if (department == null)
				return false;

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			var departmentAdmin = department.IsUserAnAdmin(userId);
			var targetGroupId = targetUserGroup?.DepartmentGroupId;
			var ancestorAdmin = !departmentAdmin && isGroupAdmin && permission.LockToGroup &&
				permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins &&
				await IsAdminOfGroupOrAncestorAsync(userId, targetGroupId);
			return ResourceVisibilityPermission.Allows(permission, departmentAdmin, isGroupAdmin,
				group?.DepartmentGroupId, targetGroupId, roles?.Select(r => r.PersonnelRoleId), ancestorAdmin);
		}

		public async Task<HashSet<string>> GetViewablePersonIdsAsync(string userId, IEnumerable<string> targetUserIds, int departmentId)
		{
			var targets = new HashSet<string>(targetUserIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
			var viewable = new HashSet<string>(StringComparer.Ordinal);
			if (targets.Count == 0)
				return viewable;

			// Everything CanUserViewPersonAsync reads that does not depend on the target is read once, in the same order.
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewGroupUsers);

			if (permission == null)
			{
				viewable.UnionWith(targets);
				return viewable;
			}

			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return viewable;

			var isGroupAdmin = group != null && group.IsUserGroupAdmin(userId);
			var departmentAdmin = department.IsUserAnAdmin(userId);
			var roleIds = roles?.Select(r => r.PersonnelRoleId).ToArray();
			var checkAncestors = !departmentAdmin && isGroupAdmin && permission.LockToGroup &&
				permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins;
			var targetGroups = await _departmentGroupsService.GetGroupIdsForAllUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, int>();
			// Targets share few groups, so the ancestor-admin answer is read once per group.
			var ancestorAdmin = new Dictionary<int, bool>();

			foreach (var target in targets)
			{
				int? targetGroupId = target != null && targetGroups.TryGetValue(target, out var groupId) ? groupId : null;
				var adminOfTarget = false;
				if (checkAncestors && targetGroupId.HasValue && !ancestorAdmin.TryGetValue(targetGroupId.Value, out adminOfTarget))
					ancestorAdmin[targetGroupId.Value] = adminOfTarget = await IsAdminOfGroupOrAncestorAsync(userId, targetGroupId);

				if (ResourceVisibilityPermission.Allows(permission, departmentAdmin, isGroupAdmin, group?.DepartmentGroupId, targetGroupId, roleIds, adminOfTarget))
					viewable.Add(target);
			}

			return viewable;
		}

		private static bool AreInSameGroup(DepartmentGroup group, DepartmentGroup otherGroup)
		{
			return group != null && otherGroup != null && group.DepartmentGroupId == otherGroup.DepartmentGroupId;
		}

		/// <summary>
		/// The locked-to-group admin rule the visibility matrices apply: an admin of the target's group, or of any
		/// group above it (an area supervisor over the stations in their area). No group means no group admin.
		/// </summary>
		private async Task<bool> IsAdminOfGroupOrAncestorAsync(string userId, int? groupId)
		{
			if (!groupId.HasValue)
				return false;

			var admins = await _departmentGroupsService.GetAllAdminsForGroupAndAncestorsAsync(groupId.Value) ?? new System.Collections.Generic.List<DepartmentGroupMember>();

			return admins.Any(a => string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));
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

			if (department == null)
				return false;

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			// "All" means every person in the department, so this is true only where ResourceVisibilityPermission would
			// admit every target: locked to group, a group admin sees their own group (and the groups below it), never all.
			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && department.IsUserAnAdmin(userId))
			{ // Department Admins only
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup && (department.IsUserAnAdmin(userId) || isGroupAdmin))
			{ // Department and group Admins (not locked to group)
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup && department.IsUserAnAdmin(userId))
			{ // Department and group Admins (locked to group): only department admins see everyone.
				return true;
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
			else if (permission.Action == (int)PermissionActions.Everyone && (!permission.LockToGroup || department.IsUserAnAdmin(userId)))
			{ // Everyone; locked to group, department admins still see everyone.
				return true;
			}

			return false;
		}

		public async Task<bool> CanUserDeleteCallAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.DeleteCall);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null || !department.IsUserInDepartment(userId))
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

			if (!await _dispatchScopeService.CanUserAccessCallAsync(departmentId, userId, call))
				return false;

			return _permissionsService.IsUserAllowed(permission, departmentId, callGroupId, userGroupId, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserCloseCallAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CloseCall);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null || !department.IsUserInDepartment(userId))
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

			if (!await _dispatchScopeService.CanUserAccessCallAsync(departmentId, userId, call))
				return false;

			return _permissionsService.IsUserAllowed(permission, departmentId, callGroupId, userGroupId, department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserAddCallDataAsync(string userId, int callId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.AddCallData);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null || !department.IsUserInDepartment(userId))
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

			if (!await _dispatchScopeService.CanUserAccessCallAsync(departmentId, userId, call))
				return false;

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
			if (userToView == userId)
				return true;

			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelCacheKey, departmentId));

			if (matrix != null && matrix.EveryoneNoGroupLock)
				return true;

			if (matrix?.Users != null && matrix.Users.TryGetValue(userToView, out var userViewList))
				return userViewList != null && userViewList.Contains(userId);

			// No matrix (cache off, expired or unreadable) or one built before this person existed: answer from the
			// permission rows. Answering "yes" here let everyone through whenever the cache was off.
			RequestMatrixRefreshForMissing(departmentId, SecurityCacheTypes.WhoCanViewPersonnel, matrix);
			return await CanUserViewPersonLiveAsync(userToView, userId, departmentId, PermissionTypes.ViewGroupUsers);
		}

		public async Task<bool> CanUserViewPersonLocationViaMatrixAsync(string userToView, string userId, int departmentId)
		{
			if (userToView == userId)
				return true;

			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUsers>(string.Format(WhoCanViewPersonnelLocationsCacheKey, departmentId));

			if (matrix != null && matrix.EveryoneNoGroupLock)
				return true;

			if (matrix?.Users != null && matrix.Users.TryGetValue(userToView, out var userViewList))
				return userViewList != null && userViewList.Contains(userId);

			RequestMatrixRefreshForMissing(departmentId, SecurityCacheTypes.WhoCanViewPersonnelLocations, matrix);
			return await CanUserViewPersonLiveAsync(userToView, userId, departmentId, PermissionTypes.CanSeePersonnelLocations);
		}

		public async Task<bool> CanUserViewUnitViaMatrixAsync(int unitToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUnits>(string.Format(WhoCanViewUnitsCacheKey, departmentId));

			if (matrix != null && matrix.EveryoneNoGroupLock)
				return true;

			if (matrix?.Units != null && matrix.Units.TryGetValue(unitToView, out var userViewList))
				return userViewList != null && userViewList.Contains(userId);

			RequestMatrixRefreshForMissing(departmentId, SecurityCacheTypes.WhoCanViewUnits, matrix);
			return await CanUserViewUnitLiveAsync(unitToView, userId, departmentId, PermissionTypes.ViewGroupUnits);
		}

		public async Task<bool> CanUserViewUnitLocationViaMatrixAsync(int unitToView, string userId, int departmentId)
		{
			var matrix = await _cacheProvider.GetAsync<VisibilityPayloadUnits>(string.Format(WhoCanViewUnitLocationsCacheKey, departmentId));

			if (matrix != null && matrix.EveryoneNoGroupLock)
				return true;

			if (matrix?.Units != null && matrix.Units.TryGetValue(unitToView, out var userViewList))
				return userViewList != null && userViewList.Contains(userId);

			RequestMatrixRefreshForMissing(departmentId, SecurityCacheTypes.WhoCanViewUnitLocations, matrix);
			return await CanUserViewUnitLiveAsync(unitToView, userId, departmentId, PermissionTypes.CanSeeUnitLocations);
		}

		public async Task<VisibilityPayloadUnits> GetLiveUnitVisibilityAsync(int departmentId, PermissionTypes permissionType)
		{
			var payload = new VisibilityPayloadUnits { GeneratedOn = DateTime.UtcNow };
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, permissionType);

			// The same "everyone" shortcut the matrix build takes: no row, or Everyone without the group lock.
			if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
			{
				payload.EveryoneNoGroupLock = true;
				return payload;
			}

			var viewers = await GetLiveDepartmentViewersAsync(departmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
			var ancestorAdmins = new Dictionary<int, HashSet<string>>();

			payload.Units = new Dictionary<int, List<string>>();
			foreach (var unit in units.Where(x => x != null && x.DepartmentId == departmentId))
				payload.Units[unit.UnitId] = await GetLiveViewerListAsync(permission, viewers, unit.StationGroupId, ancestorAdmins);

			return payload;
		}

		public async Task<VisibilityPayloadUsers> GetLivePersonnelVisibilityAsync(int departmentId, PermissionTypes permissionType)
		{
			var payload = new VisibilityPayloadUsers { GeneratedOn = DateTime.UtcNow };
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, permissionType);

			if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
			{
				payload.EveryoneNoGroupLock = true;
				return payload;
			}

			// Every member is both a target and a viewer.
			var viewers = await GetLiveDepartmentViewersAsync(departmentId);
			var ancestorAdmins = new Dictionary<int, HashSet<string>>();

			payload.Users = new Dictionary<string, List<string>>();
			foreach (var target in viewers)
				payload.Users[target.UserId] = await GetLiveViewerListAsync(permission, viewers, target.GroupId, ancestorAdmins);

			return payload;
		}

		#region Live visibility (matrix fallback)
		/// <summary>
		/// What a live visibility answer needs to know about the viewer: the permission row, the viewer's admin standing,
		/// group and roles -- the inputs <see cref="CanUserViewPersonAsync"/> and <see cref="IsUserAllowedForUnitAsync"/>
		/// read. Kept per instance because the matrix fallback runs once per row of a roster when the matrix is missing;
		/// reused only briefly in case a scope outlives a request.
		/// </summary>
		private sealed class LiveViewer
		{
			public string UserId { get; set; }
			public DateTime LoadedOn { get; set; }
			public Permission Permission { get; set; }
			public bool DepartmentFound { get; set; }
			public bool DepartmentAdmin { get; set; }
			public bool GroupAdmin { get; set; }
			public int? GroupId { get; set; }
			public int[] RoleIds { get; set; }
		}

		private static readonly TimeSpan LiveViewerLifetime = TimeSpan.FromSeconds(30);
		private readonly ConcurrentDictionary<string, LiveViewer> _liveViewers = new ConcurrentDictionary<string, LiveViewer>(StringComparer.Ordinal);

		/// <summary>
		/// A missing matrix is rebuilt only when there is a cache to rebuild it into; one that does not list the entity is
		/// always stale.
		/// </summary>
		private void RequestMatrixRefreshForMissing(int departmentId, SecurityCacheTypes type, object matrix)
		{
			if (matrix != null || Config.SystemBehaviorConfig.CacheEnabled)
				RequestMatrixRefresh(departmentId, type);
		}

		private async Task<LiveViewer> GetLiveViewerAsync(string userId, int departmentId, PermissionTypes type)
		{
			var key = $"{departmentId}_{(int)type}_{userId}";

			if (_liveViewers.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.LoadedOn < LiveViewerLifetime)
				return cached;

			var viewer = new LiveViewer { UserId = userId, LoadedOn = DateTime.UtcNow };
			viewer.Permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, type);

			// No row: everyone sees everything, and nothing else needs reading.
			if (viewer.Permission != null)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
				var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

				viewer.DepartmentFound = department != null;
				viewer.DepartmentAdmin = department != null && department.IsUserAnAdmin(userId);
				viewer.GroupAdmin = group != null && group.IsUserGroupAdmin(userId);
				viewer.GroupId = group?.DepartmentGroupId;
				viewer.RoleIds = roles?.Select(r => r.PersonnelRoleId).ToArray();
			}

			_liveViewers[key] = viewer;
			return viewer;
		}

		/// <summary>The decision <see cref="CanUserViewPersonAsync"/> and <see cref="IsUserAllowedForUnitAsync"/> make, for a target in <paramref name="targetGroupId"/>.</summary>
		private async Task<bool> IsVisibleLiveAsync(LiveViewer viewer, int? targetGroupId)
		{
			if (viewer.Permission == null)
				return true;

			if (!viewer.DepartmentFound)
				return false;

			var ancestorAdmin = !viewer.DepartmentAdmin && viewer.GroupAdmin && viewer.Permission.LockToGroup &&
				viewer.Permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins &&
				await IsAdminOfGroupOrAncestorAsync(viewer.UserId, targetGroupId);

			return ResourceVisibilityPermission.Allows(viewer.Permission, viewer.DepartmentAdmin, viewer.GroupAdmin,
				viewer.GroupId, targetGroupId, viewer.RoleIds, ancestorAdmin);
		}

		private async Task<bool> CanUserViewPersonLiveAsync(string targetUserId, string userId, int departmentId, PermissionTypes type)
		{
			var viewer = await GetLiveViewerAsync(userId, departmentId, type);

			if (viewer.Permission == null)
				return true;

			var targetGroup = await _departmentGroupsService.GetGroupForUserAsync(targetUserId, departmentId);

			return await IsVisibleLiveAsync(viewer, targetGroup?.DepartmentGroupId);
		}

		private async Task<bool> CanUserViewUnitLiveAsync(int unitId, string userId, int departmentId, PermissionTypes type)
		{
			var viewer = await GetLiveViewerAsync(userId, departmentId, type);

			if (viewer.Permission == null)
				return true;

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null || unit.DepartmentId != departmentId)
				return false;

			return await IsVisibleLiveAsync(viewer, unit.StationGroupId);
		}

		/// <summary>
		/// Every current member of the department as a viewer, read in a handful of queries for the whole department.
		/// The group is the one <see cref="IDepartmentGroupsService.GetGroupForUserAsync"/> would pick.
		/// </summary>
		private async Task<List<LiveViewer>> GetLiveDepartmentViewersAsync(int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return new List<LiveViewer>();

			var members = await _departmentsService.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>();
			var groupIds = await _departmentGroupsService.GetGroupIdsForAllUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, int>();
			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(x => x != null).GroupBy(x => x.DepartmentGroupId).ToDictionary(x => x.Key, x => x.First());
			var roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();

			var userIds = members.Where(x => x != null && !x.IsDeleted && !String.IsNullOrWhiteSpace(x.UserId)).Select(x => x.UserId).ToList();

			if (!String.IsNullOrWhiteSpace(department.ManagingUserId))
				userIds.Add(department.ManagingUserId);

			return userIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(userId =>
			{
				int? groupId = groupIds.TryGetValue(userId, out var id) ? id : (int?)null;
				var group = groupId.HasValue && groups.TryGetValue(groupId.Value, out var found) ? found : null;

				return new LiveViewer
				{
					UserId = userId,
					Permission = null,
					DepartmentFound = true,
					DepartmentAdmin = department.IsUserAnAdmin(userId),
					GroupAdmin = group != null && group.IsUserGroupAdmin(userId),
					GroupId = groupId,
					RoleIds = roles.TryGetValue(userId, out var userRoles) ? userRoles?.Select(r => r.PersonnelRoleId).ToArray() : null
				};
			}).ToList();
		}

		/// <summary>Who among <paramref name="viewers"/> may see a target in <paramref name="targetGroupId"/>.</summary>
		private async Task<List<string>> GetLiveViewerListAsync(Permission permission, List<LiveViewer> viewers, int? targetGroupId,
			Dictionary<int, HashSet<string>> ancestorAdmins)
		{
			HashSet<string> targetAdmins = null;

			if (permission.LockToGroup && permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && targetGroupId.HasValue &&
			    !ancestorAdmins.TryGetValue(targetGroupId.Value, out targetAdmins))
			{
				var admins = await _departmentGroupsService.GetAllAdminsForGroupAndAncestorsAsync(targetGroupId.Value) ?? new List<DepartmentGroupMember>();
				ancestorAdmins[targetGroupId.Value] = targetAdmins = new HashSet<string>(admins.Where(x => x?.UserId != null).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
			}

			return viewers.Where(viewer => ResourceVisibilityPermission.Allows(permission, viewer.DepartmentAdmin, viewer.GroupAdmin,
					viewer.GroupId, targetGroupId, viewer.RoleIds,
					!viewer.DepartmentAdmin && viewer.GroupAdmin && targetAdmins != null && targetAdmins.Contains(viewer.UserId)))
				.Select(viewer => viewer.UserId).ToList();
		}
		#endregion Live visibility (matrix fallback)

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

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
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

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserCreateWorkflowAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CreateWorkflow);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserManageWorkflowCredentialAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ManageWorkflowCredentials);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserViewWorkflowRunsAsync(string userId, int departmentId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewWorkflowRuns);

			bool isGroupAdmin = false;
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (group != null)
				isGroupAdmin = group.IsUserGroupAdmin(userId);

			return _permissionsService.IsUserAllowed(permission, department != null && department.IsUserAnAdmin(userId), isGroupAdmin, roles);
		}

		public async Task<bool> CanUserModifyDepartmentAsync(string userId, int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			if (department == null)
				return false;

			return department.IsUserAnAdmin(userId);
		}

		public async Task<bool> CanUserCheckInToCalendarEventAsync(string userId, int calendarItemId)
		{
			var item = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

			if (item == null || string.IsNullOrWhiteSpace(userId))
				return false;

			// The event's own department, with its full roster. GetDepartmentByUserIdAsync returns whichever
			// department the user's active/default membership lands on first, and only the caller's row.
			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);

			if (!IsCurrentCalendarDepartmentMember(department, userId))
				return false;

			// Check-in disabled for this event
			if (item.CheckInType == (int)CalendarItemCheckInTypes.Disabled)
				return false;

			// Self check-in mode: any department member can check themselves in
			if (item.CheckInType == (int)CalendarItemCheckInTypes.SelfCheckIn)
				return true;

			// Admin-only mode: only creator, dept admin, or group admin can perform check-ins
			if (item.CheckInType == (int)CalendarItemCheckInTypes.AdminOnly)
			{
				if (IsCalendarDepartmentAdmin(department, userId))
					return true;

				if (IsCalendarItemCreator(item, userId))
					return true;

				var group = await _departmentGroupsService.GetGroupForUserAsync(userId, department.DepartmentId);
				if (group != null && group.IsUserGroupAdmin(userId))
					return true;

				return false;
			}

			return false;
		}

		public async Task<bool> CanUserAdminCheckInCalendarEventAsync(string userId, int calendarItemId, string targetUserId)
		{
			var item = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

			if (item == null || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(targetUserId))
				return false;

			// Check-in disabled for this event
			if (item.CheckInType == (int)CalendarItemCheckInTypes.Disabled)
				return false;

			// The event's department with every member row: the target has to be found in it, and the
			// target need not be an attendee. (GetDepartmentByUserIdAsync carries the caller's row only,
			// so every target looked like a non-member.)
			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);

			if (!IsCurrentCalendarDepartmentMember(department, userId))
				return false;

			// Removed and disabled members cannot be checked in
			if (!IsCurrentCalendarDepartmentMember(department, targetUserId))
				return false;

			// Department admins can check in users in their department
			if (IsCalendarDepartmentAdmin(department, userId))
				return true;

			// Calendar item creator can check in users in their department
			if (IsCalendarItemCreator(item, userId))
				return true;

			// Group admins can check in users in their group or child groups
			return await IsUserInCalendarAdminGroupScopeAsync(userId, department.DepartmentId, targetUserId);
		}

		public async Task<bool> CanUserEditCalendarCheckInAsync(string userId, string checkInId)
		{
			var checkIn = await _calendarService.GetCheckInByIdAsync(checkInId);

			if (checkIn == null || string.IsNullOrWhiteSpace(userId))
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(checkIn.DepartmentId);

			if (!IsCurrentCalendarDepartmentMember(department, userId))
				return false;

			if (string.Equals(checkIn.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return true;

			if (IsCalendarDepartmentAdmin(department, userId))
				return true;

			// Calendar item creator can edit check-ins
			var item = await _calendarService.GetCalendarItemByIdAsync(checkIn.CalendarItemId);
			if (item != null && IsCalendarItemCreator(item, userId))
				return true;

			// Group admins can edit check-ins for their group members
			return await IsUserInCalendarAdminGroupScopeAsync(userId, department.DepartmentId, checkIn.UserId);
		}

		/// <summary>
		/// Whether the user holds a current membership (not removed, not disabled) in the department. The
		/// managing user always counts, as in <see cref="Department.IsUserInDepartment"/>.
		/// </summary>
		private static bool IsCurrentCalendarDepartmentMember(Department department, string userId)
		{
			if (department == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase))
				return true;

			return department.Members != null && department.Members.Any(m =>
				string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase)
				&& DepartmentMemberStateHelper.IsCurrentMember(m, department.DepartmentId));
		}

		/// <summary>
		/// Admin test over current rows only: the full roster also carries removed rows, and removal leaves
		/// IsAdmin set.
		/// </summary>
		private static bool IsCalendarDepartmentAdmin(Department department, string userId)
		{
			if (department == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase))
				return true;

			return department.Members != null && department.Members.Any(m =>
				m.IsAdmin.GetValueOrDefault()
				&& string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase)
				&& DepartmentMemberStateHelper.IsCurrentMember(m, department.DepartmentId));
		}

		private static bool IsCalendarItemCreator(CalendarItem item, string userId)
		{
			return !string.IsNullOrWhiteSpace(item.CreatorUserId)
				&& string.Equals(item.CreatorUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Whether the user is a group admin and the target is a member of that group or one of its child groups.
		/// </summary>
		private async Task<bool> IsUserInCalendarAdminGroupScopeAsync(string userId, int departmentId, string targetUserId)
		{
			var adminGroup = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			if (adminGroup == null || !adminGroup.IsUserGroupAdmin(userId))
				return false;

			if (IsUserInCalendarGroup(adminGroup, targetUserId))
				return true;

			var childGroups = await _departmentGroupsService.GetAllChildDepartmentGroupsAsync(adminGroup.DepartmentGroupId);
			if (childGroups != null)
			{
				foreach (var childGroup in childGroups)
				{
					if (IsUserInCalendarGroup(childGroup, targetUserId))
						return true;
				}
			}

			return false;
		}

		private static bool IsUserInCalendarGroup(DepartmentGroup group, string userId)
		{
			return group?.Members != null
				&& group.Members.Any(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		public async Task<bool> CanUserDeleteCalendarCheckInAsync(string userId, string checkInId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var checkIn = await _calendarService.GetCheckInByIdAsync(checkInId);

			if (department == null || checkIn == null)
				return false;

			if (checkIn.DepartmentId != department.DepartmentId)
				return false;

			if (department.IsUserAnAdmin(userId))
				return true;

			// Calendar item creator can delete check-ins
			var item = await _calendarService.GetCalendarItemByIdAsync(checkIn.CalendarItemId);
			if (item != null && !string.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == userId)
				return true;

			return false;
		}

		public async Task<bool> CanUserViewCalendarCheckInsAsync(string userId, int calendarItemId)
		{
			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var item = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

			if (department == null || item == null)
				return false;

			if (item.DepartmentId != department.DepartmentId)
				return false;

			return true;
		}

	}
}
