using System;
using System.Threading;
using System.Threading.Tasks;
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
		private readonly IInventoryService _inventoryService;

		public DeleteService(IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			ICallsService callsService, IActionLogsService actionLogsService, IUsersService usersService,
			IUserProfileService userProfileService, IMessageService messageService, IDepartmentGroupsService departmentGroupsService,
						IWorkLogsService workLogsService, IUserStateService userStateService, IPersonnelRolesService personnelRolesService, IDistributionListsService distributionListsService,
			IShiftsService shiftsService, IUnitsService unitsService, ICertificationService certificationService, ILogService logService, IInventoryService inventoryService)
		{
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_actionLogsService = actionLogsService;
			_usersService = usersService;
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
			_inventoryService = inventoryService;
		}

		public async Task<DeleteUserResults> DeleteUserAsync(int departmentId, string authorizingUserId, string userIdToDelete)
		{
			if (!await _authorizationService.CanUserDeleteUserAsync(departmentId, authorizingUserId, userIdToDelete))
				return DeleteUserResults.UnAuthroized;

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userIdToDelete);

			if (department.ManagingUserId == userIdToDelete)
				return DeleteUserResults.UserIsManagingDepartmentAdmin;

			var member = await  _departmentsService.GetDepartmentMemberAsync(userIdToDelete, departmentId);
			member.IsDeleted = true;
			await _departmentsService.SaveDepartmentMemberAsync(member);

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

		public async Task<DeleteGroupResults> DeleteGroupAsync(int departmentGroupId, int departmentId, string currentUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!await _authorizationService.CanUserEditDepartmentGroupAsync(currentUserId, departmentGroupId))
				return DeleteGroupResults.UnAuthroized;

			await _callsService.ClearGroupForDispatchesAsync(departmentGroupId, cancellationToken);
			await _workLogsService.ClearGroupForLogsAsync(departmentGroupId, cancellationToken);
			await _unitsService.ClearGroupForUnitsAsync(departmentGroupId, cancellationToken);
			await _shiftsService.DeleteShiftGroupsByGroupIdAsync(departmentGroupId, cancellationToken);
			await _inventoryService.DeleteInventoriesByGroupIdAsync(departmentGroupId, departmentId, cancellationToken);
			await _departmentGroupsService.DeleteGroupMembersByGroupIdAsync(departmentGroupId, departmentId, cancellationToken);
			await _departmentGroupsService.DeleteGroupByIdAsync(departmentGroupId, cancellationToken);

			return DeleteGroupResults.NoFailure;
		}
	}
}
