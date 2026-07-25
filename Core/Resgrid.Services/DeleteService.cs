using System;
using System.Collections.Generic;
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
		private readonly IAuditLogsRepository _auditLogsRepository;

		public DeleteService(IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			ICallsService callsService, IActionLogsService actionLogsService, IUsersService usersService,
			IUserProfileService userProfileService, IMessageService messageService, IDepartmentGroupsService departmentGroupsService,
			IWorkLogsService workLogsService, IUserStateService userStateService, IPersonnelRolesService personnelRolesService,
			IDistributionListsService distributionListsService, IShiftsService shiftsService, IUnitsService unitsService,
			ICertificationService certificationService, ILogService logService, IInventoryService inventoryService,
			IEventAggregator eventAggregator, IAddressService addressService, IQueueService queueService, IEmailService emailService,
			IDeleteRepository deleteRepository, IAuditLogsRepository auditLogsRepository)
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
			_auditLogsRepository = auditLogsRepository;
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

			// Only one pending deletion request per department; don't stack duplicates (and their emails).
			var existingRequest = await _queueService.GetPendingDeleteDepartmentQueueItemAsync(departmentId);
			if (existingRequest != null)
				return DeleteDepartmentResults.NoFailure;

			var result = await _queueService.EnqueuePendingDeleteDepartmentAsync(departmentId, authorizingUserId, cancellationToken);

			var auditEvent = new AuditEvent();
			auditEvent.Before = null;
			auditEvent.DepartmentId = departmentId;
			auditEvent.UserId = authorizingUserId;
			auditEvent.Type = AuditLogTypes.DeleteDepartmentRequested;
			auditEvent.After = result?.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = ipAddress;
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = userAgent;
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			if (result != null)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				await SendDeleteDepartmentEmailToAllAdminsAsync(department, result);
			}

			return DeleteDepartmentResults.NoFailure;
		}

		public async Task<DeleteDepartmentResults> HandlePendingDepartmentDeletionRequestAsync(QueueItem item, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!int.TryParse(item.SourceId, out var departmentId))
			{
				Logging.LogError($"DeleteService::Pending department deletion QueueItemId {item.QueueItemId} has a malformed SourceId '{item.SourceId}'; setting a terminal state so it is not retried.");
				await SetTerminalQueueItemStateAsync(item, $"Department deletion failed: malformed SourceId '{item.SourceId}'.", cancellationToken);

				return DeleteDepartmentResults.Failure;
			}

			if (!item.ToBeCompletedOn.HasValue)
				return DeleteDepartmentResults.Failure;

			var now = DateTime.UtcNow;

			if (!await _authorizationService.CanUserDeleteDepartmentAsync(item.QueuedByUserId, departmentId))
			{
				// A past-due item whose requester is no longer the managing user can never execute;
				// finalize it so it stops looping in the pending set (a current admin can still
				// cancel a not-yet-due item from department settings instead).
				if (now >= item.ToBeCompletedOn.Value)
					await SetTerminalQueueItemStateAsync(item, $"Department deletion abandoned: requesting user {item.QueuedByUserId} is no longer the managing user.", cancellationToken);

				return DeleteDepartmentResults.UnAuthorized;
			}

			if (now >= item.ToBeCompletedOn.Value)
			{
				/*
				 * You have a pending department deletion request and it can be executed now.
				 */

				try
				{
					Logging.LogInfo($"DeleteService::Executing pending department deletion for DepartmentId {item.SourceId}, requested by UserId {item.QueuedByUserId} on {item.QueuedOn:u}, scheduled for {item.ToBeCompletedOn:u}");

					var result = await _deleteRepository.DeleteDepartmentAndUsersAsync(departmentId);

					// Write the execution audit record only after the delete succeeds: retried
					// attempts must not each leave an "executed" row. The row is written directly
					// (not via the queued audit events, which resolve the now-deleted user profile
					// when processed), and AuditLogs are not deleted with the department, so it
					// survives as the durable trail for the actual deletion.
					await WriteDepartmentDeletionExecutedAuditLogAsync(item, departmentId, cancellationToken);

					item.CompletedOn = DateTime.UtcNow;
					item.Data = "Department deletion executed by the system.";
					var result2 = await _queueService.UpdateQueueItem(item, cancellationToken);
				}
				catch (Exception e)
				{
					Logging.LogException(e);
					Logging.SendExceptionEmail(e, "DeleteDepartment", departmentId);

					// Bounded retry: a transient failure (DB timeout/deadlock) must not permanently
					// abort a scheduled deletion. The item stays pending (CompletedOn null) and is
					// retried on the next poll until the attempt budget is exhausted; only then set
					// a terminal state so it stops re-queuing.
					const int maxAttempts = 5;
					item.AttemptCount += 1;

					if (item.AttemptCount >= maxAttempts)
					{
						await SetTerminalQueueItemStateAsync(item, $"Department deletion permanently failed after {item.AttemptCount} attempts: {e.Message}", cancellationToken);
					}
					else
					{
						item.Data = $"Department deletion attempt {item.AttemptCount} failed: {e.Message}";

						try
						{
							await _queueService.UpdateQueueItem(item, cancellationToken);
						}
						catch (Exception updateEx)
						{
							// Persisting the retry state failed; the item stays pending and is retried
							// next poll regardless, but don't let this mask the original exception or
							// abort the worker's processing of other pending items.
							Logging.LogException(updateEx, $"DeleteService::Failed to persist retry state for department deletion QueueItemId {item.QueueItemId}");
						}
					}

					return DeleteDepartmentResults.Failure;
				}
			}
			else if (now.Date >= item.ToBeCompletedOn.Value.Date && item.ReminderCount < 3)
			{
				/*
				 * Deletion is scheduled for today (final reminder).
				 */
				await SendDeleteDepartmentReminderAndMarkAsync(item, departmentId, 3, cancellationToken);
			}
			else if (now >= item.ToBeCompletedOn.Value.AddDays(-5) && item.ReminderCount < 2)
			{
				/*
				 * Deletion is within 5 days and the 5-day reminder has not been sent yet.
				 */
				await SendDeleteDepartmentReminderAndMarkAsync(item, departmentId, 2, cancellationToken);
			}
			else if (now >= item.ToBeCompletedOn.Value.AddDays(-14) && item.ReminderCount < 1)
			{
				/*
				 * Deletion is within 14 days and the 14-day reminder has not been sent yet.
				 */
				await SendDeleteDepartmentReminderAndMarkAsync(item, departmentId, 1, cancellationToken);
			}

			return DeleteDepartmentResults.NoFailure;
		}

		private async Task SendDeleteDepartmentReminderAndMarkAsync(QueueItem item, int departmentId, int reminderLevel, CancellationToken cancellationToken)
		{
			await SendDeleteDepartmentReminderToAllAdminsAsync(item, departmentId);
			item.ReminderCount = reminderLevel;
			await _queueService.UpdateQueueItem(item, cancellationToken);
		}

		private async Task SendDeleteDepartmentReminderToAllAdminsAsync(QueueItem item, int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			await SendDeleteDepartmentEmailToAllAdminsAsync(department, item);
		}

		private async Task SetTerminalQueueItemStateAsync(QueueItem item, string data, CancellationToken cancellationToken)
		{
			try
			{
				item.CompletedOn = DateTime.UtcNow;
				item.Data = data;
				await _queueService.UpdateQueueItem(item, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DeleteService::Failed to set terminal state on failed department deletion QueueItemId {item.QueueItemId}");
			}
		}

		private async Task SendDeleteDepartmentEmailToAllAdminsAsync(Department department, QueueItem item)
		{
			if (department == null || item == null)
				return;

			// Notify the managing member AND every department admin, deduped, so no single
			// person is the only one who knows a deletion is pending.
			var adminUserIds = new List<string>();

			if (!String.IsNullOrWhiteSpace(department.ManagingUserId))
				adminUserIds.Add(department.ManagingUserId);

			if (department.AdminUsers != null)
				adminUserIds.AddRange(department.AdminUsers);

			foreach (var adminUserId in adminUserIds.Distinct())
			{
				try
				{
					var adminUserProfile = await _userProfileService.GetProfileByUserIdAsync(adminUserId);

					if (adminUserProfile?.User == null || String.IsNullOrWhiteSpace(adminUserProfile.User.Email))
						continue;

					await _emailService.SendDeleteDepartmentEmail(adminUserProfile.User.Email, adminUserProfile.FullName.AsFirstNameLastName, item);
				}
				catch (Exception ex)
				{
					// A single bad recipient must not block notifications to the remaining admins.
					Logging.LogException(ex, $"DeleteService::Failed to send department deletion email to UserId {adminUserId} for DepartmentId {item.SourceId}");
				}
			}
		}

		private async Task WriteDepartmentDeletionExecutedAuditLogAsync(QueueItem item, int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var auditLog = new AuditLog();
				auditLog.DepartmentId = departmentId;
				auditLog.UserId = item.QueuedByUserId;
				auditLog.LogType = (int)AuditLogTypes.DeleteDepartmentRequestExecuted;
				auditLog.Message = $"The system executed the pending department deletion request for department id {item.SourceId}";
				auditLog.Data = $"QueueItemId: {item.QueueItemId}; RequestedByUserId: {item.QueuedByUserId}; QueuedOn (UTC): {item.QueuedOn:u}; ScheduledFor (UTC): {item.ToBeCompletedOn:u}; RemindersSent: {item.ReminderCount}";
				auditLog.Successful = true;
				auditLog.IpAddress = null;
				auditLog.UserAgent = null;
				auditLog.ServerName = Environment.MachineName;
				auditLog.LoggedOn = DateTime.UtcNow;

				await _auditLogsRepository.SaveOrUpdateAsync(auditLog, cancellationToken);
			}
			catch (Exception ex)
			{
				// Auditing must never block the deletion itself.
				Logging.LogException(ex, $"DeleteService::Failed to write department deletion executed audit log for DepartmentId {item.SourceId}");
			}
		}
	}
}
