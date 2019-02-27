using System;
using System.Transactions;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DeleteService : IDeleteService
	{
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IPushUriService _pushUriService;
		private readonly IMessageService _messageService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IWorkLogsService _workLogsService;
		private readonly IUserStateService _userStateService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDistributionListsService _distributionListsService;
		private readonly IShiftsService _shiftsService;
		private readonly IUnitsService _unitsService;
		private readonly ICertificationService _certificationService;
		private readonly ILogService _logService;

		public DeleteService(IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			ICallsService callsService, IActionLogsService actionLogsService, IUsersService usersService,
			IPushUriService pushUriService, IUserProfileService userProfileService, IMessageService messageService, IDepartmentGroupsService departmentGroupsService,
						IWorkLogsService workLogsService, IUserStateService userStateService, IPersonnelRolesService personnelRolesService, IDistributionListsService distributionListsService,
			IShiftsService shiftsService, IUnitsService unitsService, ICertificationService certificationService, ILogService logService)
		{
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_actionLogsService = actionLogsService;
			_usersService = usersService;
			_pushUriService = pushUriService;
			_userProfileService = userProfileService;
			_messageService = messageService;
			_departmentGroupsService = departmentGroupsService;
			_workLogsService = workLogsService;
			_userStateService = userStateService;
			_personnelRolesService = personnelRolesService;
			_distributionListsService = distributionListsService;
			_shiftsService = shiftsService;
			_unitsService = unitsService;
			_certificationService = certificationService;
			_logService = logService;
		}

		public DeleteUserResults DeleteUser(int departmentId, string authroizingUserId, string userIdToDelete)
		{
			if (!_authorizationService.CanUserDeleteUser(departmentId, authroizingUserId, userIdToDelete))
				return DeleteUserResults.UnAuthroized;

			var department = _departmentsService.GetDepartmentByUserId(userIdToDelete);

			if (department.ManagingUserId == userIdToDelete)
				return DeleteUserResults.UserIsManagingDepartmentAdmin;

			var member = _departmentsService.GetDepartmentMember(userIdToDelete, departmentId);
			member.IsDeleted = true;
			_departmentsService.SaveDepartmentMember(member);

			//_certificationService.DeleteAllCertificationsForUser(userIdToDelete);
			//_distributionListsService.RemoveUserFromAllLists(userIdToDelete);
			//_personnelRolesService.RemoveUserFromAllRoles(userIdToDelete);
			//_userStateService.DeleteStatesForUser(userIdToDelete);
			//_workLogsService.ClearInvestigationByLogsForUser(userIdToDelete);
			//_workLogsService.DeleteLogsForUser(userIdToDelete, department.ManagingUserId);
			//_messageService.DeleteMessagesForUser(userIdToDelete);
			////_userProfileService.DeletProfileForUser(userIdToDelete);
			//_pushUriService.DeletePushUrisForUser(userIdToDelete);
			//_actionLogsService.DeleteActionLogsForUser(userIdToDelete);
			//_callsService.DeleteDispatchesForUserAndRemapCalls(department.ManagingUserId, userIdToDelete);
			//_departmentGroupsService.DeleteUserFromGroups(userIdToDelete);
			//_usersService.DeleteUser(userIdToDelete);

			return DeleteUserResults.NoFailure;
		}

		public DeleteGroupResults DeleteGroup(int departmentGroupId, string currentUserId)
		{
			if (!_authorizationService.CanUserEditDepartmentGroup(currentUserId, departmentGroupId))
				return DeleteGroupResults.UnAuthroized;

			_callsService.ClearGroupForDispatches(departmentGroupId);
			_workLogsService.ClearGroupForLogs(departmentGroupId);
			_unitsService.ClearGroupForUnits(departmentGroupId);
			_shiftsService.DeleteShiftGroupsByGroupId(departmentGroupId);
			_departmentGroupsService.DeleteGroupById(departmentGroupId);

			return DeleteGroupResults.NoFailure;
		}
	}
}
