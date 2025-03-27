using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
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
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
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

									IF (SELECT COUNT(*) FROM DepartmentMembers WHERE UserId = @UserId) = 1
									BEGIN
										-- This user is only a member of one department so clear their account out as well
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
								DELETE FROM [dbo].[CommandDefinitions] WHERE DepartmentId = @DepartmentId
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
								DELETE FROM [dbo].[Departments] WHERE DepartmentId = @DepartmentId

								-- Delete the managing member's user
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
								DELETE FROM [dbo].[DepartmentMembers] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[DepartmentGroupMembers] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[DistributionListMembers] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[PersonnelRoleUsers] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[PushUris] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[UnitStateRoles] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[CallDispatches] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[AspNetUserClaims] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[AspNetUserLogins] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[AspNetUserRoles] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[AspNetUsersExt] WHERE UserId = @ManagingUserId
								DELETE FROM [dbo].[AspNetUsers] WHERE Id = @ManagingUserId
						",
							new { DepartmentId = departmentId });

						transaction.Commit();
					}
				}

				return false;
			}

			return false;
		}
	}
}
