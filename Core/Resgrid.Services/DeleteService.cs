using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
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
		private readonly IEventAggregator _eventAggregator;
		private readonly IAddressService _addressService;
		private readonly IQueueService _queueService;
		private readonly IEmailService _emailService;
		private readonly IDeleteRepository _deleteRepository;

		public DeleteService(IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			ICallsService callsService, IActionLogsService actionLogsService, IUsersService usersService,
			IUserProfileService userProfileService, IMessageService messageService, IDepartmentGroupsService departmentGroupsService,
			IWorkLogsService workLogsService, IUserStateService userStateService, IPersonnelRolesService personnelRolesService,
			IDistributionListsService distributionListsService, IShiftsService shiftsService, IUnitsService unitsService,
			ICertificationService certificationService, ILogService logService, IInventoryService inventoryService,
			IEventAggregator eventAggregator, IAddressService addressService, IQueueService queueService, IEmailService emailService,
			IDeleteRepository deleteRepository)
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
			_eventAggregator = eventAggregator;
			_addressService = addressService;
			_queueService = queueService;
			_emailService = emailService;
			_deleteRepository = deleteRepository;
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

		public async Task<DeleteUserResults> DeleteUserAccountAsync(int departmentId, string authorizingUserId, string userIdToDelete, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			//if (!await _authorizationService.CanUserDeleteUserAsync(departmentId, authorizingUserId, userIdToDelete))
			//	return DeleteUserResults.UnAuthroized;

			if (authorizingUserId != userIdToDelete)
				return DeleteUserResults.UnAuthroized;

			var departments = await _departmentsService.GetAllDepartmentsForUserAsync(userIdToDelete);

			if (departments != null && departments.Any())
			{
				foreach (var dm in departments)
				{
					var dep = await _departmentsService.GetDepartmentByUserIdAsync(userIdToDelete);

					if (dep.ManagingUserId == userIdToDelete)
						return DeleteUserResults.UserIsManagingDepartmentAdmin;


					var auditEvent = new AuditEvent();
					auditEvent.Before = dm.CloneJsonToString();
					auditEvent.DepartmentId = dm.DepartmentId;
					auditEvent.UserId = userIdToDelete;
					auditEvent.Type = AuditLogTypes.UserAccountDeleted;

					dm.IsDeleted = true;
					dm.IsAdmin = false;
					dm.IsHidden = true;
					dm.IsDefault = false;
					dm.IsActive = false;
					dm.IsDisabled = true;

					auditEvent.After = dm.CloneJsonToString();
					auditEvent.Successful = true;
					auditEvent.IpAddress = ipAddress;
					auditEvent.ServerName = Environment.MachineName;
					auditEvent.UserAgent = userAgent;
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					await _departmentsService.SaveDepartmentMemberAsync(dm, cancellationToken);
				}
			}

			var userProfile = await _userProfileService.GetProfileByUserIdAsync(userIdToDelete, true);

			if (userProfile != null)
			{
				userProfile.MobileCarrier = 0;
				userProfile.MobileNumber = null;
				userProfile.SendPush = false;
				userProfile.SendEmail = false;
				userProfile.SendSms = false;
				userProfile.SendMessageEmail = false;
				userProfile.SendMessagePush = false;
				userProfile.SendMessageSms = false;
				userProfile.SendNotificationEmail = false;
				userProfile.SendNotificationPush = false;
				userProfile.SendNotificationSms = false;
				userProfile.DoNotRecieveNewsletters = true;
				userProfile.HomeNumber = null;
				userProfile.VoiceForCall = false;
				userProfile.VoiceCallHome = false;
				userProfile.VoiceCallMobile = false;

				if (userProfile.HomeAddressId.HasValue)
					await _addressService.DeleteAddress(userProfile.HomeAddressId.Value, cancellationToken);

				if (userProfile.MailingAddressId.HasValue)
					await _addressService.DeleteAddress(userProfile.MailingAddressId.Value, cancellationToken);

				await _userProfileService.SaveProfileAsync(departmentId, userProfile, cancellationToken);
			}

			await _usersService.ClearOutUserLoginAsync(userIdToDelete);

			return DeleteUserResults.NoFailure;
		}

		public async Task<DeleteGroupResults> DeleteGroupAsync(int departmentGroupId, int departmentId, string currentUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!await _authorizationService.CanUserEditDepartmentGroupAsync(currentUserId, departmentGroupId))
				return DeleteGroupResults.UnAuthorized;

			await _callsService.ClearGroupForDispatchesAsync(departmentGroupId, cancellationToken);
			await _workLogsService.ClearGroupForLogsAsync(departmentGroupId, cancellationToken);
			await _unitsService.ClearGroupForUnitsAsync(departmentGroupId, cancellationToken);
			await _shiftsService.DeleteShiftGroupsByGroupIdAsync(departmentGroupId, cancellationToken);
			await _inventoryService.DeleteInventoriesByGroupIdAsync(departmentGroupId, departmentId, cancellationToken);
			await _departmentGroupsService.DeleteGroupMembersByGroupIdAsync(departmentGroupId, departmentId, cancellationToken);
			await _departmentGroupsService.DeleteGroupByIdAsync(departmentGroupId, cancellationToken);

			return DeleteGroupResults.NoFailure;
		}

		public async Task<DeleteDepartmentResults> DeleteDepartment(int departmentId, string authorizingUserId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!await _authorizationService.CanUserDeleteDepartmentAsync(authorizingUserId, departmentId))
				return DeleteDepartmentResults.UnAuthorized;

			var auditEvent = new AuditEvent();
			auditEvent.Before = null;
			auditEvent.DepartmentId = departmentId;
			auditEvent.UserId = authorizingUserId;
			auditEvent.Type = AuditLogTypes.DeleteDepartmentRequested;
			auditEvent.After = null;
			auditEvent.Successful = true;
			auditEvent.IpAddress = ipAddress;
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = userAgent;
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			var result = await _queueService.EnqueuePendingDeleteDepartmentAsync(departmentId, authorizingUserId, cancellationToken);

			if (result != null)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

				var ownerUserProfile = await _userProfileService.GetProfileByUserIdAsync(authorizingUserId);
				var result2 = await _emailService.SendDeleteDepartmentEmail(ownerUserProfile.User.Email, ownerUserProfile.FullName.AsFirstNameLastName, result);

				foreach (var adminUser in department.AdminUsers)
				{
					var adminUserProfile = await _userProfileService.GetProfileByUserIdAsync(adminUser);
					var result1 = await _emailService.SendDeleteDepartmentEmail(adminUserProfile.User.Email, adminUserProfile.FullName.AsFirstNameLastName, result);
				}
			}

			return DeleteDepartmentResults.NoFailure;
		}

		public async Task<DeleteDepartmentResults> HandlePendingDepartmentDeletionRequestAsync(QueueItem item, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!await _authorizationService.CanUserDeleteDepartmentAsync(item.QueuedByUserId, int.Parse(item.SourceId)))
				return DeleteDepartmentResults.UnAuthorized;

			if (item.ToBeCompletedOn.HasValue &&  DateTime.UtcNow >= item.ToBeCompletedOn.Value.AddDays(-10) && item.ReminderCount == 0)
			{
				/*
				 * You have a pending department deletion request, it is within 10 days out and we have no yet sent a reminder.
				 */

				var department = await _departmentsService.GetDepartmentByIdAsync(int.Parse(item.SourceId));

				var ownerUserProfile = await _userProfileService.GetProfileByUserIdAsync(item.QueuedByUserId);
				var result2 = await _emailService.SendDeleteDepartmentEmail(ownerUserProfile.User.Email, ownerUserProfile.FullName.AsFirstNameLastName, item);

				foreach (var adminUser in department.AdminUsers)
				{
					var adminUserProfile = await _userProfileService.GetProfileByUserIdAsync(adminUser);
					var result1 = await _emailService.SendDeleteDepartmentEmail(adminUserProfile.User.Email, adminUserProfile.FullName.AsFirstNameLastName, item);
				}

				item.ReminderCount += 1;
				var result = await _queueService.UpdateQueueItem(item, cancellationToken);
			}
			else if (item.ToBeCompletedOn.HasValue && DateTime.UtcNow >= item.ToBeCompletedOn.Value)
			{
				/*
				 * You have a pending department deletion request and it can be executed now.
				 */

				try
				{
					var result = await _deleteRepository.DeleteDepartmentAndUsersAsync(int.Parse(item.SourceId));

					item.CompletedOn = DateTime.UtcNow;
					var result2 = await _queueService.UpdateQueueItem(item, cancellationToken);
				}
				catch (Exception e)
				{
					Logging.LogException(e);
					Logging.SendExceptionEmail(e, "DeleteDepartment", int.Parse(item.SourceId));

					return DeleteDepartmentResults.Failure;
				}
			}

			return DeleteDepartmentResults.NoFailure;
		}
	}
}
