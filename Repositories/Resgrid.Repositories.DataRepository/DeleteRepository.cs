using System;
using System.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Dapper.Contrib.Extensions;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	public class DeleteRepository : IDeleteRepository
	{
		public async Task<bool> DeleteDepartmentAndUsersAsync(int departmentId)
		{
			Dapper.SqlMapper.Settings.CommandTimeout = 300;

			// TODO: Ok this needs to be revisited and also made compatible with PgSql. -SJ 3-26-2025

			if (Config.DataConfig.DatabaseType == DatabaseTypes.SqlServer)
			{
				using (var db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					await db.OpenAsync();

					using (var transaction = db.BeginTransaction())
					{
						var result = await db.ExecuteAsync(@"
								DECLARE @UserId NVARCHAR(128)
								DECLARE @UnitId INT
								DECLARE @ManagingUserId NVARCHAR(128)

								SET @ManagingUserId = (SELECT ManagingUserId FROM [dbo].[Departments] WHERE DepartmentId = @DepartmentId)

								DECLARE db_cursor CURSOR FOR
								SELECT UserId
								FROM [dbo].[DepartmentMembers]
								WHERE DepartmentId = @DepartmentId AND UserId != @ManagingUserId

								DECLARE unit_cursor CURSOR FOR
								SELECT UnitId
								FROM [dbo].[Units]
								WHERE DepartmentId = @DepartmentId

								-- Child rows of data the cursors below delete piecemeal; remove while parents still exist
								DELETE FROM [dbo].[ScheduledTaskLogs] WHERE ScheduledTaskId IN (SELECT ScheduledTaskId FROM [dbo].[ScheduledTasks] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[LogAttachments] WHERE LogId IN (SELECT LogId FROM [dbo].[Logs] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[LogUnits] WHERE LogId IN (SELECT LogId FROM [dbo].[Logs] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[LogUsers] WHERE LogId IN (SELECT LogId FROM [dbo].[Logs] WHERE DepartmentId = @DepartmentId)

								-- PushUris has no DepartmentId column; delete by membership while DepartmentMembers rows still exist
								DELETE FROM [dbo].[PushUris] WHERE UserId IN (SELECT UserId FROM [dbo].[DepartmentMembers] WHERE DepartmentId = @DepartmentId)

								OPEN db_cursor
								FETCH NEXT FROM db_cursor INTO @UserId

								-- Clear all the users out in the department
								WHILE @@FETCH_STATUS = 0
								BEGIN
									DELETE FROM [dbo].[ScheduledTasks] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
								    DELETE FROM [dbo].[UserStates] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
								    DELETE FROM [dbo].[Logs] WHERE LoggedByUserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[MessageRecipients] WHERE UserId = @UserId
									DELETE FROM [dbo].[MessageRecipients] WHERE UserId = @UserId
									DELETE FROM [dbo].[MessageRecipients] WHERE MessageId IN (SELECT MessageId FROM [dbo].[Messages] WHERE ReceivingUserId = @UserId)
									DELETE FROM [dbo].[MessageRecipients] WHERE MessageId IN (SELECT MessageId FROM [dbo].[Messages] WHERE SendingUserId = @UserId)
									DELETE FROM [dbo].[Messages] WHERE SendingUserId = @UserId
									DELETE FROM [dbo].[Messages] WHERE ReceivingUserId = @UserId
									DELETE FROM [dbo].[PersonnelCertifications] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
								    DELETE FROM [dbo].[PersonnelRoleUsers] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[PushUris] WHERE UserId = @UserId
									DELETE FROM [dbo].[UserStates] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[ActionLogs] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[DepartmentMembers] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[DepartmentGroupMembers] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[DistributionListMembers] WHERE UserId = @UserId
									DELETE FROM [dbo].[PersonnelRoleUsers] WHERE UserId = @UserId AND DepartmentId = @DepartmentId
								    DELETE FROM [dbo].[UnitStateRoles] WHERE UserId = @UserId --AND DepartmentId = @DepartmentId
									DELETE FROM [dbo].[CallDispatches] WHERE UserId = @UserId --AND DepartmentId = @DepartmentId

									IF (SELECT COUNT(*) FROM DepartmentMembers WHERE UserId = @UserId) = 0
									BEGIN
										-- The deleted membership was the user's last, so clear their account out as well
										DELETE FROM [dbo].[ChatbotUserIdentities] WHERE UserId = @UserId
										DELETE FROM [dbo].[ChatbotLinkingCodes] WHERE UserId = @UserId
										DELETE FROM [dbo].[UserProfiles] WHERE UserId = @UserId
										DELETE FROM [dbo].[AspNetUserClaims] WHERE UserId = @UserId
										DELETE FROM [dbo].[AspNetUserLogins] WHERE UserId = @UserId
										DELETE FROM [dbo].[AspNetUserRoles] WHERE UserId = @UserId
										DELETE FROM [dbo].[AspNetUsersExt] WHERE UserId = @UserId
										DELETE FROM [dbo].[AspNetUsers] WHERE Id = @UserId
									END

									FETCH NEXT FROM db_cursor INTO @UserId
								END

								CLOSE db_cursor
								DEALLOCATE db_cursor

								OPEN unit_cursor
								FETCH NEXT FROM unit_cursor INTO @UnitId

								-- Clear all the unit out in the department
								WHILE @@FETCH_STATUS = 0
								BEGIN
									DELETE FROM [dbo].[UnitLocations] WHERE UnitId = @UnitId
									DELETE FROM [dbo].[UnitLogs] WHERE UnitId = @UnitId
									DELETE FROM [dbo].[UnitActiveRoles] WHERE UnitId = @UnitId
									DELETE FROM [dbo].[UnitRoles] WHERE UnitId = @UnitId
									DELETE FROM [dbo].[UnitStates] WHERE UnitId = @UnitId
									DELETE FROM [dbo].[Inventories] WHERE UnitId  = @UnitId
									DELETE FROM [dbo].[LogUsers] WHERE UnitId  = @UnitId
									DELETE FROM [dbo].[Units] WHERE UnitId = @UnitId

									FETCH NEXT FROM unit_cursor INTO @UnitId
								END

								CLOSE unit_cursor
								DEALLOCATE unit_cursor

								-- Delete all the department level data
								-- Call child data (parents deleted further down)
								DELETE FROM [dbo].[CallAttachments] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallNotes] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallDispatches] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallDispatchGroups] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallDispatchRoles] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallDispatchUnits] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallUnits] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallProtocols] WHERE CallId IN (SELECT CallId FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CallLogs] WHERE DepartmentId = @DepartmentId

								-- Command definitions reference CallTypes (CallTypeId), so their tree must go first
								DELETE FROM [dbo].[CommandDefinitionRolePersonnelRoles] WHERE CommandDefinitionRoleId IN (SELECT CommandDefinitionRoleId FROM [dbo].[CommandDefinitionRoles] WHERE CommandDefinitionId IN (SELECT CommandDefinitionId FROM [dbo].[CommandDefinitions] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[CommandDefinitionRoleUnitTypes] WHERE CommandDefinitionRoleId IN (SELECT CommandDefinitionRoleId FROM [dbo].[CommandDefinitionRoles] WHERE CommandDefinitionId IN (SELECT CommandDefinitionId FROM [dbo].[CommandDefinitions] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[CommandDefinitionRoles] WHERE CommandDefinitionId IN (SELECT CommandDefinitionId FROM [dbo].[CommandDefinitions] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CommandDefinitions] WHERE DepartmentId = @DepartmentId

								DELETE FROM [dbo].[CallTypes] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CallVideoFeeds] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentCallEmails] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentCallPriorities] WHERE DepartmentId = @DepartmentId

								-- Shift tree (Shifts row deleted further down)
								DELETE FROM [dbo].[ShiftSignupTradeUserShifts] WHERE ShiftSignupId IN (SELECT ShiftSignupId FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[ShiftSignupTradeUsers] WHERE ShiftSignupTradeId IN (SELECT ShiftSignupTradeId FROM [dbo].[ShiftSignupTrades] WHERE SourceShiftSignupId IN (SELECT ShiftSignupId FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)) OR TargetShiftSignupId IN (SELECT ShiftSignupId FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)))
								DELETE FROM [dbo].[ShiftSignupTrades] WHERE SourceShiftSignupId IN (SELECT ShiftSignupId FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)) OR TargetShiftSignupId IN (SELECT ShiftSignupId FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[ShiftSignups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftGroupAssignments] WHERE ShiftGroupId IN (SELECT ShiftGroupId FROM [dbo].[ShiftGroups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[ShiftGroupRoles] WHERE ShiftGroupId IN (SELECT ShiftGroupId FROM [dbo].[ShiftGroups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[ShiftGroups] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftDays] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftAdmins] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftPersons] WHERE ShiftId IN (SELECT ShiftId FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftStaffingPersons] WHERE ShiftStaffingId IN (SELECT ShiftStaffingId FROM [dbo].[ShiftStaffings] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ShiftStaffings] WHERE DepartmentId = @DepartmentId

								-- Training tree (Trainings row deleted further down)
								DELETE FROM [dbo].[TrainingQuestionAnswers] WHERE TrainingQuestionId IN (SELECT TrainingQuestionId FROM [dbo].[TrainingQuestions] WHERE TrainingId IN (SELECT TrainingId FROM [dbo].[Trainings] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[TrainingQuestions] WHERE TrainingId IN (SELECT TrainingId FROM [dbo].[Trainings] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[TrainingAttachments] WHERE TrainingId IN (SELECT TrainingId FROM [dbo].[Trainings] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[TrainingUsers] WHERE TrainingId IN (SELECT TrainingId FROM [dbo].[Trainings] WHERE DepartmentId = @DepartmentId)

								-- Dispatch protocol tree (DispatchProtocols row deleted further down)
								DELETE FROM [dbo].[DispatchProtocolQuestionAnswers] WHERE DispatchProtocolQuestionId IN (SELECT DispatchProtocolQuestionId FROM [dbo].[DispatchProtocolQuestions] WHERE DispatchProtocolId IN (SELECT DispatchProtocolId FROM [dbo].[DispatchProtocols] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[DispatchProtocolQuestions] WHERE DispatchProtocolId IN (SELECT DispatchProtocolId FROM [dbo].[DispatchProtocols] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DispatchProtocolAttachments] WHERE DispatchProtocolId IN (SELECT DispatchProtocolId FROM [dbo].[DispatchProtocols] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DispatchProtocolTriggers] WHERE DispatchProtocolId IN (SELECT DispatchProtocolId FROM [dbo].[DispatchProtocols] WHERE DepartmentId = @DepartmentId)

								-- Calendar
								DELETE FROM [dbo].[CalendarItemAttendees] WHERE CalendarItemId IN (SELECT CalendarItemId FROM [dbo].[CalendarItems] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CalendarItems] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CalendarItemTypes] WHERE DepartmentId = @DepartmentId

								-- Custom statuses
								DELETE FROM [dbo].[CustomStateDetails] WHERE CustomStateId IN (SELECT CustomStateId FROM [dbo].[CustomStates] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[CustomStates] WHERE DepartmentId = @DepartmentId

								-- Mapping / POIs
								DELETE FROM [dbo].[Pois] WHERE PoiTypeId IN (SELECT PoiTypeId FROM [dbo].[POITypes] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[POITypes] WHERE DepartmentId = @DepartmentId

								-- Resource orders (ResourceOrders row deleted further down)
								DELETE FROM [dbo].[ResourceOrderFillUnits] WHERE ResourceOrderFillId IN (SELECT ResourceOrderFillId FROM [dbo].[ResourceOrderFills] WHERE DepartmentId = @DepartmentId OR ResourceOrderItemId IN (SELECT ResourceOrderItemId FROM [dbo].[ResourceOrderItems] WHERE ResourceOrderId IN (SELECT ResourceOrderId FROM [dbo].[ResourceOrders] WHERE DepartmentId = @DepartmentId)))
								DELETE FROM [dbo].[ResourceOrderFills] WHERE DepartmentId = @DepartmentId OR ResourceOrderItemId IN (SELECT ResourceOrderItemId FROM [dbo].[ResourceOrderItems] WHERE ResourceOrderId IN (SELECT ResourceOrderId FROM [dbo].[ResourceOrders] WHERE DepartmentId = @DepartmentId))
								DELETE FROM [dbo].[ResourceOrderItems] WHERE ResourceOrderId IN (SELECT ResourceOrderId FROM [dbo].[ResourceOrders] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[ResourceOrderSettings] WHERE DepartmentId = @DepartmentId

								-- Department public profile tree
								DELETE FROM [dbo].[DepartmentProfileArticles] WHERE DepartmentProfileId IN (SELECT DepartmentProfileId FROM [dbo].[DepartmentProfiles] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DepartmentProfileInvites] WHERE DepartmentProfileId IN (SELECT DepartmentProfileId FROM [dbo].[DepartmentProfiles] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DepartmentProfileMessages] WHERE DepartmentProfileId IN (SELECT DepartmentProfileId FROM [dbo].[DepartmentProfiles] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DepartmentProfileUserFollows] WHERE DepartmentProfileId IN (SELECT DepartmentProfileId FROM [dbo].[DepartmentProfiles] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DepartmentProfiles] WHERE DepartmentId = @DepartmentId

								-- User-defined fields
								DELETE FROM [dbo].[UdfFieldValues] WHERE UdfDefinitionId IN (SELECT UdfDefinitionId FROM [dbo].[UdfDefinitions] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[UdfFields] WHERE UdfDefinitionId IN (SELECT UdfDefinitionId FROM [dbo].[UdfDefinitions] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[UdfDefinitions] WHERE DepartmentId = @DepartmentId

								-- Weather alerts
								DELETE FROM [dbo].[WeatherAlertZones] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[WeatherAlerts] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[WeatherAlertSources] WHERE DepartmentId = @DepartmentId

								-- Communication tests
								DELETE FROM [dbo].[CommunicationTestResults] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CommunicationTestRuns] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CommunicationTestTargets] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CommunicationTests] WHERE DepartmentId = @DepartmentId

								-- Chatbot
								DELETE FROM [dbo].[ChatbotMessageLog] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[ChatbotDepartmentConfigs] WHERE DepartmentId = @DepartmentId

								-- Remaining department-scoped tables
								DELETE FROM [dbo].[AuditLogs] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Automations] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentCertificationTypes] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentFiles] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Files] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentNotifications] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentSecurityPolicies] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentSsoConfigs] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DocumentCategories] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[NoteCategories] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[FeatureFlagOverrides] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[FeatureFlagUsages] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[GdprDataExportRequests] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[NotificationAlerts] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Permissions] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Ranks] WHERE DepartmentId = @DepartmentId

								-- Catch-alls for rows the per-user cursor missed (users removed from the
								-- department before deletion, or rows with no surviving member)
								DELETE FROM [dbo].[ScheduledTasks] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[UserStates] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[PersonnelCertifications] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[PersonnelRoleUsers] WHERE DepartmentId = @DepartmentId

								DELETE FROM [dbo].[Invites] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Payments] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[ActionLogs] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Inventories] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[InventoryTypes] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Logs] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Calls] WHERE DepartmentId = @DepartmentId
								--DELETE FROM [dbo].[Addresses] WHERE AddressId = (SELECT AddressId FROM [dbo].[Departments] WHERE DepartmentId = @DepartmentId)
								--DELETE FROM [dbo].[Addresses] WHERE AddressId IN (SELECT AddressId FROM [dbo].[DepartmentGroups] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[DepartmentGroupMembers] WHERE DepartmentGroupId IN (SELECT DepartmentGroupId FROM [dbo].[DepartmentGroups] WHERE DepartmentId = @DepartmentId)
								DELETE FROM [dbo].[Shifts] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentGroups] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentSettings] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DistributionLists] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Documents] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Invites] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Notes] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Payments] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[PersonnelRoles] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[UnitTypes] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DispatchProtocols] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Forms] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[PaymentAddons] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[ResourceOrders] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Trainings] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentLinks] WHERE LinkedDepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentLinks] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentVoiceChannels] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentVoices] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[CallQuickTemplates] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[DepartmentCallPruning] WHERE DepartmentId = @DepartmentId
								DELETE FROM [dbo].[Departments] WHERE DepartmentId = @DepartmentId

								-- Remove only this department's managing membership. The same user may
								-- manage or belong to another department and must retain that account.
								DELETE FROM [dbo].[DepartmentMembers] WHERE UserId = @ManagingUserId AND DepartmentId = @DepartmentId

								IF (SELECT COUNT(*) FROM DepartmentMembers WHERE UserId = @ManagingUserId) = 0
								BEGIN
									DELETE FROM [dbo].[ScheduledTasks] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[UserStates] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[Logs] WHERE LoggedByUserId = @ManagingUserId
									DELETE FROM [dbo].[MessageRecipients] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[MessageRecipients] WHERE MessageId IN (SELECT MessageId FROM [dbo].[Messages] WHERE ReceivingUserId = @ManagingUserId)
									DELETE FROM [dbo].[MessageRecipients] WHERE MessageId IN (SELECT MessageId FROM [dbo].[Messages] WHERE SendingUserId = @ManagingUserId)
									DELETE FROM [dbo].[Messages] WHERE ReceivingUserId = @ManagingUserId
									DELETE FROM [dbo].[Messages] WHERE SendingUserId = @ManagingUserId
									DELETE FROM [dbo].[PersonnelCertifications] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[PersonnelRoleUsers] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[PushUris] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[UserProfiles] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[UserStates] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[ActionLogs] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[DepartmentGroupMembers] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[DistributionListMembers] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[PersonnelRoleUsers] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[PushUris] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[UnitStateRoles] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[CallDispatches] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[ChatbotUserIdentities] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[ChatbotLinkingCodes] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[AspNetUserClaims] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[AspNetUserLogins] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[AspNetUserRoles] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[AspNetUsersExt] WHERE UserId = @ManagingUserId
									DELETE FROM [dbo].[AspNetUsers] WHERE Id = @ManagingUserId
								END
						",
							new { DepartmentId = departmentId }, transaction);

						transaction.Commit();
					}
				}

				return true;
			}

			return false;
		}
	}
}
