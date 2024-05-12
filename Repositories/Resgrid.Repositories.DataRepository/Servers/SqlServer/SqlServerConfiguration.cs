﻿using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calendar;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using Resgrid.Repositories.DataRepository.Queries.DepartmentGroups;
using Resgrid.Repositories.DataRepository.Queries.Departments;
using Resgrid.Repositories.DataRepository.Queries.Logs;
using Resgrid.Repositories.DataRepository.Queries.Mapping;
using Resgrid.Repositories.DataRepository.Queries.Notes;
using Resgrid.Repositories.DataRepository.Queries.Plans;
using Resgrid.Repositories.DataRepository.Queries.Shifts;
using Resgrid.Repositories.DataRepository.Queries.Trainings;
using Resgrid.Repositories.DataRepository.Queries.Units;
using Resgrid.Repositories.DataRepository.Queries.UserProfiles;
using Resgrid.Repositories.DataRepository.Queries.UserStates;

namespace Resgrid.Repositories.DataRepository.Servers.SqlServer
{
	public class SqlServerConfiguration : SqlConfiguration
	{
		public SqlServerConfiguration()
		{
			ParameterNotation = "@";
			SchemaName = "[dbo]";
			TableColumnStartNotation = "[";
			TableColumnEndNotation = "]";
			InsertGetReturnIdCommand =
				"; SELECT CAST(SCOPE_IDENTITY() as int);"; // For Postgresql INSERT INTO persons (lastname,firstname) VALUES ('Smith', 'John') RETURNING id; https://stackoverflow.com/questions/2944297/postgresql-function-for-last-inserted-id/2944481

			#region Common Queries

			InsertQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)%RETURNID%";
			DeleteQuery = "DELETE FROM %SCHEMA%.%TABLENAME% WHERE [%IDCOLUMN%] = %ID%";
			DeleteMultipleQuery =
				"DELETE FROM %SCHEMA%.%BASETABLENAME% WHERE [%PARENTKEYNAME%] = %PARENTID% AND [%IDCOLUMN%] NOT IN (%IDS%)";
			UpdateQuery = "UPDATE %SCHEMA%.%TABLENAME% %SETVALUES% WHERE [%IDCOLUMN%] = %ID%";
			SelectByIdQuery = "SELECT * FROM %SCHEMA%.%BASETABLENAME% WHERE [%IDCOLUMN%] = %ID%";
			SelectAllQuery = "SELECT * FROM %SCHEMA%.%TABLENAME%";
			SelectByDepartmentIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID%";
			SelectByUserIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %ID%";

			#endregion Common Queries

			#region Action Logs

			ActionLogsTable = "ActionLogs";
			SelectLastActionLogsForDepartmentQuery = @"
							SELECT al.*, u.*
							FROM %SCHEMA%.%ACTIONLOGSTABLE% al
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON u.[Id] = al.[UserId]
							INNER JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% dm ON dm.[UserId] = al.[UserId]
							WHERE al.DepartmentId = %DID% AND dm.IsDeleted = 0 AND
							(%DAA% = 1 OR al.Timestamp >= %TS%) AND
							dm.IsDisabled = 0 AND dm.IsHidden = 0 AND al.Timestamp >= %LTS%";
			SelectActionLogsByUserIdQuery = @"
					SELECT %SCHEMA%.%ACTIONLOGSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%ACTIONLOGSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%ACTIONLOGSTABLE%.UserId
					[UserId] = %ID%";
			SelectALogsByUserInDateRangQuery = @"
					SELECT %SCHEMA%.%ACTIONLOGSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%ACTIONLOGSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%ACTIONLOGSTABLE%.UserId
					WHERE [UserId] = %USERID% AND [Timestamp] >= %STARTDATE% AND [Timestamp] <= %ENDDATE%";
			SelectALogsByDateRangeQuery = @"
					SELECT %SCHEMA%.%ACTIONLOGSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%ACTIONLOGSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%ACTIONLOGSTABLE%.UserId
					WHERE [DepartmentId] = %DID% AND [Timestamp] >= %STARTDATE% AND [Timestamp] <= %ENDDATE%";
			SelectALogsByDidQuery = @"
					SELECT al.*, u.*
					FROM %SCHEMA%.%ACTIONLOGSTABLE% al
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON u.Id = al.UserId
					WHERE al.DepartmentId = %DID%";
			SelectLastActionLogForUserQuery = @"
							SELECT TOP 1 al.*, u.*
							FROM %SCHEMA%.%ACTIONLOGSTABLE% al
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON u.[Id] = al.[UserId]
							WHERE al.[UserId] = %USERID% AND (%DAA% = 1 OR al.[Timestamp] >= %TS%)
							ORDER BY ActionLogId DESC";
			SelectActionLogsByCallIdTypeQuery = @"
							SELECT al.*, u.*
							FROM %SCHEMA%.%ACTIONLOGSTABLE% al
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON u.Id = al.UserId
							WHERE al.[DestinationId] = %CALLID%  AND (al.[ActionTypeId] IS NULL OR al.[ActionTypeId] IN (%TYPES%))";
			SelectPreviousActionLogsByUserQuery = @"
							SELECT %SCHEMA%.%ACTIONLOGSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.*
							FROM %SCHEMA%.%ACTIONLOGSTABLE% a1
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%ACTIONLOGSTABLE%.UserId
							WHERE a1.UserId = %USERID% AND [ActionLogId] < %ACTIONLOGID%";
			SelectLastActionLogByUserIdQuery = @"
							SELECT TOP 1 %SCHEMA%.%ACTIONLOGSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.*
							FROM %SCHEMA%.%ACTIONLOGSTABLE% a1
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%ACTIONLOGSTABLE%.UserId
							WHERE a1.UserId = %USERID%
							ORDER BY ActionLogId DESC";
			SelectActionLogsByCallIdQuery = @"
					SELECT al.*, u.*
					FROM %SCHEMA%.%ACTIONLOGSTABLE% al
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON u.Id = al.UserId
					WHERE al.DestinationId = %DID%";

			#endregion ActionLogs

			#region Department Members

			DepartmentMembersTable = "DepartmentMembers";
			DepartmentCallPruningTable = "DepartmentCallPruning";

			SelectMembersUnlimitedQuery = @"
					SELECT %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId
					WHERE [DepartmentId] = %ID% AND [IsDeleted] = 0";

			SelectMembersUnlimitedInclDelQuery = @"
					SELECT %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId
					WHERE [DepartmentId] = %ID%";

			//SelectMembersWithinLimitsQuery = @"DECLARE @limit INT
			//				SET @limit = (SELECT TOP 1 (pl.LimitValue * p.Quantity) FROM Payments p
			//				INNER JOIN PlanLimits pl ON pl.PlanId = p.PlanId
			//				WHERE DepartmentId = %ID% AND pl.LimitType = 1 AND p.EffectiveOn <= GETUTCDATE() AND p.EndingOn >= GETUTCDATE()
			//				ORDER BY PaymentId DESC)


			//				SELECT TOP (@limit) %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
			//				FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE%
			//				INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId
			//				WHERE [DepartmentId] = %ID% AND [IsDeleted] = 0";

			SelectMembersWithinLimitsQuery = @"
							SELECT %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
							FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE%
							INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId
							WHERE [DepartmentId] = %ID% AND [IsDeleted] = 0";


			SelectMembersByDidUserIdQuery = @"
					SELECT %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId
					WHERE [UserId] = %USERID% AND [DepartmentId] = %DID%";

			SelectMembersByUserIdQuery = @"
					SELECT dm.*, d.*
					FROM %SCHEMA%.%DEPARTMENTMEMBERSTABLE% dm
					INNER JOIN %SCHEMA%.%DEPARTMENTSTABLE% d ON d.[DepartmentId] = dm.[DepartmentId]
					WHERE dm.[UserId] = %USERID%";

			#endregion Department Members

			#region Department Settings

			DepartmentSettingsTable = "DepartmentSettings";
			SelectDepartmentSettingByDepartmentIdTypeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND SettingType = %SETTINGTYPE%";
			SelectDepartmentSettingByTypeUserIdQuery = @"SELECT ds.* FROM [DepartmentSettings] ds
						INNER JOIN [DepartmentMembers] dm ON ds.DepartmentId = dm.DepartmentId
						WHERE dm.UserId = %USERID% AND ds.SettingType = %SETTINGTYPE%";
			SelectDepartmentSettingBySettingAndTypeQuery = @"SELECT ds.* FROM %SCHEMA%.%TABLENAME% ds
						WHERE ds.Setting = %SETTING% AND ds.SettingType = %SETTINGTYPE%";
			SelectAllDepartmentManagerInfoQuery = @"SELECT d.DepartmentId, d.Name, up.FirstName, up.LastName, u.Email
						FROM [Departments] d
						INNER JOIN [AspNetUsers] u ON u.Id = d.ManagingUserId
						LEFT OUTER JOIN [UserProfiles] up ON up.UserId = d.ManagingUserId";
			SelectDepartmentManagerInfoByEmailQuery =
				@"SELECT d.DepartmentId, d.Name, up.FirstName, up.LastName, u.Email
						FROM [Departments] d
						INNER JOIN [AspNetUsers] u ON u.Id = d.ManagingUserId
						LEFT OUTER JOIN [UserProfiles] up ON up.UserId = d.ManagingUserId
						WHERE u.Email = %EMAILADDRESS%";

			#endregion Department Settings

			#region Invites

			InvitesTable = "Invites";
			SelectInviteByCodeQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [Code] = %CODE%";
			SelectInviteByEmailQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [EmailAddress] = %EMAIL%";

			#endregion Invites

			#region Queues

			QueueItemsTable = "QueueItems";
			SelectQueueItemByTypeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [QueueType] = %TYPE% AND PickedUp IS NULL && CompletedOn IS NULL";

			#endregion Queues

			#region Certifications

			CertificationsTable = "PersonnelCertifications";

			SelectCertsByUserQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID%";

			#endregion Certifications

			#region Permissions

			PermissionsTable = "Permissions";

			SelectPermissionByDepartmentTypeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [PermissionType] = %TYPE%";

			#endregion Permissions

			#region Department Links

			DepartmentLinksTable = "DepartmentLinks";

			SelectAllLinksForDepartmentQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% OR [LinkedDepartmentId] = %DID%";
			SelectAllLinkForDepartmentIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% OR [DepartmentLinkId] = %ID%";

			#endregion Department Links

			#region Departments

			DepartmentsTable = "Departments";
			SelectDepartmentByLinkCodeQuery = @"
					SELECT %SCHEMA%.%DEPARTMENTSTABLE%.*, %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*
						FROM %SCHEMA%.%DEPARTMENTSTABLE%
						LEFT JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% ON %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.[DepartmentId] =  %SCHEMA%.%DEPARTMENTSTABLE%.[DepartmentId]
					WHERE [LinkCode] = %CODE%";
			SelectDepartmentByIdQuery = @"
					SELECT d.*, dt.*
					FROM %SCHEMA%.%DEPARTMENTSTABLE% d
					LEFT JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% dt ON dt.[DepartmentId] =  d.[DepartmentId]
					WHERE d.[DepartmentId] = %DID%";
			SelectCallPruningByDidQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID%";
			SelectDepartmentReportByDidQuery = @"SELECT
							d.DepartmentId,
							d.Name,
							d.CreatedOn,
							(SELECT COUNT (*) FROM DepartmentGroups dg WHERE dg.DepartmentId = d.DepartmentId) AS 'Groups',
							(SELECT COUNT (*) -1 FROM DepartmentMembers dm WHERE dm.DepartmentId = d.DepartmentId) AS 'Users',
							(SELECT COUNT (*) FROM Units u WHERE u.DepartmentId = d.DepartmentId) AS 'Units',
							(SELECT COUNT (*) FROM Calls c WHERE c.DepartmentId = d.DepartmentId) AS 'Calls',
							(SELECT COUNT (*) FROM PersonnelRoles pr WHERE pr.DepartmentId = d.DepartmentId) AS 'Roles',
							(SELECT COUNT (*) FROM DepartmentNotifications dn WHERE dn.DepartmentId = d.DepartmentId) AS 'Notifications',
							(SELECT COUNT (*) FROM UnitTypes ut WHERE ut.DepartmentId = d.DepartmentId) AS 'UnitTypes',
							(SELECT COUNT (*) FROM CallTypes ct WHERE ct.DepartmentId = d.DepartmentId) AS 'CallTypes',
							(SELECT COUNT (*) FROM DepartmentCertificationTypes dct WHERE dct.DepartmentId = d.DepartmentId) AS 'CertTypes',
								CASE
									WHEN d.TimeZone IS NOT NULL OR d.Use24HourTime IS NOT NULL OR d.AddressId IS NOT NULL
										THEN 1
									ELSE 0
										END AS 'Settings'
						FROM Departments d
						WHERE d.DepartmentId = %DID%";
			SelectDepartmentByNameQuery = @"
					SELECT %SCHEMA%.%DEPARTMENTSTABLE%.*, %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.*
						FROM %SCHEMA%.%DEPARTMENTSTABLE%
						LEFT JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% ON %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.[DepartmentId] =  %SCHEMA%.%DEPARTMENTSTABLE%.[DepartmentId]
					WHERE [Name] = %NAME%";
			SelectDepartmentByUsernameQuery = @"SELECT d.*, dm.*
							FROM AspNetUsers u
							INNER JOIN DepartmentMembers dm1 ON dm1.UserId = u.Id
							INNER JOIN Departments d ON d.DepartmentId = dm1.DepartmentId
							INNER JOIN DepartmentMembers dm ON dm.DepartmentId = d.DepartmentId
							WHERE u.UserName = %USERNAME% AND d.DepartmentId = dm.DepartmentId AND dm.IsDeleted = 0 AND (dm.IsActive = 1 OR dm.IsDefault = 1)";
			SelectDepartmentByUserIdQuery =@"SELECT d.*, dm.*
							FROM DepartmentMembers dm1
							INNER JOIN Departments d ON d.DepartmentId = dm1.DepartmentId
							INNER JOIN DepartmentMembers dm ON dm.DepartmentId = d.DepartmentId
							WHERE dm.UserId = %USERID% AND d.DepartmentId = dm.DepartmentId AND dm.IsDeleted = 0 AND (dm.IsActive = 1 OR dm.IsDefault = 1)";
			SelectValidDepartmentByUsernameQuery =
				@"SELECT dm.UserId as 'UserId', dm.IsDisabled as 'IsDisabled', dm.IsDeleted as 'IsDeleted', d.DepartmentId as 'DepartmentId', d.Code as 'Code'
							FROM AspNetUsers u
							INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
							INNER JOIN Departments d ON dm.DepartmentId = d.DepartmentId
							WHERE u.UserName = %USERNAME% AND dm.IsActive = 1";
			SelectDepartmentStatsByUserDidQuery = @"
					SELECT
					(SELECT COUNT(*) FROM %SCHEMA%.%MESSAGESTABLE% m
					LEFT OUTER JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% mr ON m.MessageId = mr.MessageId
					WHERE mr.[UserId] = %USERID% AND mr.[IsDeleted] = 0 AND m.[IsDeleted] = 0) AS [UnreadMessageCount],

					(SELECT COUNT(*) FROM %SCHEMA%.%CALLTABLENAME% c WHERE c.[DepartmentId] = %DID% AND c.[IsDeleted] = 0 AND c.[State] = 0) AS [OpenCallsCount]";

			#endregion Departments

			#region Personnel Roles

			PersonnelRolesTable = "PersonnelRoles";
			PersonnelRoleUsersTable = "PersonnelRoleUsers";
			SelectRoleByDidAndNameQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [Name] = %NAME%";
			SelectRolesByDidAndUserQuery = @"
					SELECT * FROM [PersonnelRoles] pr
					INNER JOIN [PersonnelRoleUsers] pru ON pr.[PersonnelRoleId] = pru.[PersonnelRoleId]
					WHERE pru.[UserId] = %USERID% AND pr.[DepartmentId] = %DID%";
			//SelectRoleUsersByRoleQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [PersonnelRoleId] = %ROLEID%";
			SelectRoleUsersByRoleQuery = @"
					SELECT * FROM [PersonnelRoleUsers] pru
					INNER JOIN [dbo].DepartmentMembers dm ON dm.UserId = pru.UserId AND dm.[DepartmentId] = pru.[DepartmentId]
					WHERE pru.[PersonnelRoleId] = %ROLEID% AND dm.[IsDisabled] = 0 AND dm.[IsDeleted] = 0";

			SelectRoleUsersByUserQuery = @"
					SELECT * FROM [PersonnelRoleUsers] pru
					INNER JOIN [PersonnelRoles] pr ON pru.[PersonnelRoleId] = pr.[PersonnelRoleId]
					WHERE pru.[UserId] = %USERID% AND pr.[DepartmentId] = %DID%";
			SelectRoleUsersByDidQuery = @"
					SELECT * FROM [PersonnelRoleUsers] pru
					INNER JOIN [PersonnelRoles] pr ON pru.[PersonnelRoleId] = pr.[PersonnelRoleId]
					WHERE pr.[DepartmentId] = %DID%";
			SelectRolesByDidQuery = @"
					SELECT pr.*, pru.*
					FROM %SCHEMA%.%ROLESTABLE% pr
					LEFT JOIN %SCHEMA%.%ROLEUSERSTABLE% pru ON pr.[PersonnelRoleId] = pru.[PersonnelRoleId]
					WHERE pr.[DepartmentId] = %DID%";
			SelectRolesByRoleIdQuery = @"
					SELECT pr.*, pru.*
					FROM %SCHEMA%.%ROLESTABLE% pr
					LEFT JOIN %SCHEMA%.%ROLEUSERSTABLE% pru ON pr.[PersonnelRoleId] = pru.[PersonnelRoleId]
					WHERE pr.[PersonnelRoleId] = %ROLEID%";

			#endregion Personnel Roles

			#region Inventory

			InventoryTable = "Inventories";
			InventoryTypesTable = "InventoryTypes";
			SelectInventoryByTypeIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [TypeId] = %TYPEID%";
			SelectInventoryByDIdQuery = @"
					SELECT i.*, it.*
					FROM %SCHEMA%.%INVENTORYTABLE% i
					INNER JOIN %SCHEMA%.%INVENTORYTYPESTABE% it ON it.[InventoryTypeId] = i.[TypeId]
					WHERE i.[DepartmentId] = %DID%";
			SelectInventoryByInventoryIdQuery = @"
					SELECT i.*, it.*
					FROM %SCHEMA%.%INVENTORYTABLE% i
					INNER JOIN %SCHEMA%.%INVENTORYTYPESTABE% it ON it.[InventoryTypeId] = i.[TypeId]
					WHERE i.[InventoryId] = %INVENTORYID%";
			DeleteInventoryByGroupIdQuery = @"
					DELETE FROM %SCHEMA%.%TABLENAME%
					WHERE DepartmentId = %DID% AND GroupId = %ID%";
			#endregion Inventory

			#region Messages

			MessagesTable = "Messages";
			MessageRecipientsTable = "MessageRecipients";
			SelectInboxMessagesByUserQuery = @"
					SELECT m.*, mr.*
					FROM %SCHEMA%.%MESSAGESTABLE% m
					LEFT JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% mr ON mr.[MessageId] = m.[MessageId]
					WHERE mr.[UserId] = %USERID% AND mr.[IsDeleted] = 0";
			SelectUnreadMessageCountQuery = @"
					SELECT COUNT(*) FROM %SCHEMA%.%MESSAGESTABLE% m
					LEFT OUTER JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% mr ON m.MessageId = mr.MessageId
					WHERE mr.[UserId] = %USERID% AND mr.[IsDeleted] = 0 AND m.[IsDeleted] = 0";
			SelectMessageRecpByMessageUsQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [MessageId] = %MESSAGEID% AND [UserId] = %USERID%";
			SelectMessagesByUserQuery = @"
					SELECT %SCHEMA%.%MESSAGESTABLE%.*, %SCHEMA%.%MESSAGERECIPIENTSTABLE%.*
					FROM %SCHEMA%.%MESSAGESTABLE%
					LEFT JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% ON %SCHEMA%.%MESSAGERECIPIENTSTABLE%.[MessageId] =  %SCHEMA%.%MESSAGESTABLE%.[MessageId]
					WHERE [ReceivingUserId] = %USERID% OR [SendingUserId] = %USERID%";
			SelectMessageRecpsByUserQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID%";
			SelectMessageByIdQuery = @"
					SELECT m.*, mr.*
					FROM %SCHEMA%.%MESSAGESTABLE% m
					LEFT JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% mr ON mr.[MessageId] =  m.[MessageId]
					WHERE m.[MessageId] = %MESSAGEID%";
			SelectSentMessagesByUserQuery = @"
					SELECT m.*, mr.*
					FROM %SCHEMA%.%MESSAGESTABLE% m
					LEFT JOIN %SCHEMA%.%MESSAGERECIPIENTSTABLE% mr ON mr.[MessageId] =  m.[MessageId]
					WHERE m.[SendingUserId] = %USERID% AND m.[IsDeleted] = 0";
			UpdateRecievedMessagesAsDeletedQuery = @"
					UPDATE %SCHEMA%.%TABLENAME%
					SET [IsDeleted] = 1
					WHERE [MessageId] IN (%MESSAGEIDS%) AND [UserId] = %USERID%";
			UpdateRecievedMessagesAsReadQuery = @"
					UPDATE %SCHEMA%.%TABLENAME%
					SET [ReadOn] = %READON%
					WHERE [MessageId] IN (%MESSAGEIDS%) AND [UserId] = %USERID%";

			#endregion Messages

			#region Identity

			InsertRoleQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)";
			DeleteRoleQuery = "DELETE FROM %SCHEMA%.%TABLENAME% WHERE [Id] = %ID%";
			UpdateRoleQuery = "UPDATE %SCHEMA%.%TABLENAME% %SETVALUES% WHERE [Id] = %ID%";
			SelectRoleByNameQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [Name] = %NAME%";
			SelectRoleByIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [Id] = %ID%";
			InsertUserQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% OUTPUT INSERTED.Id VALUES(%VALUES%)";
			DeleteUserQuery = "DELETE FROM %SCHEMA%.%TABLENAME% WHERE [Id] = %ID%";
			UpdateUserQuery = "UPDATE %SCHEMA%.%TABLENAME% %SETVALUES% WHERE [Id] = %ID%";
			SelectUserByUserNameQuery =
				"SELECT %SCHEMA%.%USERTABLE%.*, %SCHEMA%.%USERROLETABLE%.* FROM %SCHEMA%.%USERTABLE% LEFT JOIN %SCHEMA%.%USERROLETABLE% ON %SCHEMA%.%USERROLETABLE%.[UserId] =  %SCHEMA%.%USERTABLE%.[Id] WHERE [UserName] = %USERNAME%";
			SelectUserByEmailQuery =
				"SELECT %SCHEMA%.%USERTABLE%.*, %SCHEMA%.%USERROLETABLE%.* FROM %SCHEMA%.%USERTABLE% LEFT JOIN %SCHEMA%.%USERROLETABLE% ON %SCHEMA%.%USERROLETABLE%.[UserId] =  %SCHEMA%.%USERTABLE%.[Id] WHERE [Email] = %EMAIL%";
			SelectUserByIdQuery =
				"SELECT %SCHEMA%.%USERTABLE%.*, %SCHEMA%.%USERROLETABLE%.* FROM %SCHEMA%.%USERTABLE% LEFT JOIN %SCHEMA%.%USERROLETABLE% ON %SCHEMA%.%USERROLETABLE%.[UserId] =  %SCHEMA%.%USERTABLE%.[Id] WHERE %SCHEMA%.%USERTABLE%.[Id] = %ID%";
			InsertUserClaimQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)";
			InsertUserLoginQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)";
			InsertUserRoleQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)";
			GetUserLoginByLoginProviderAndProviderKeyQuery =
				"SELECT TOP 1 %USERFILTER%, %SCHEMA%.%USERROLETABLE%.* FROM %SCHEMA%.%USERTABLE% LEFT JOIN %SCHEMA%.%USERROLETABLE% ON %SCHEMA%.%USERROLETABLE%.[UserId] = %SCHEMA%.%USERTABLE%.[Id] INNER JOIN %SCHEMA%.%USERLOGINTABLE% ON %SCHEMA%.%USERTABLE%.[Id] = %SCHEMA%.%USERLOGINTABLE%.[UserId] WHERE [LoginProvider] = @LoginProvider AND [ProviderKey] = @ProviderKey";
			GetClaimsByUserIdQuery = "SELECT [ClaimType], [ClaimValue] FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %ID%";
			GetRolesByUserIdQuery =
				"SELECT [Name] FROM %SCHEMA%.%ROLETABLE%, %SCHEMA%.%USERROLETABLE% WHERE [UserId] = %ID% AND %SCHEMA%.%ROLETABLE%.[Id] = %SCHEMA%.%USERROLETABLE%.[RoleId]";
			GetUserLoginInfoByIdQuery =
				"SELECT [LoginProvider], [Name], [ProviderKey] FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %ID%";
			GetUsersByClaimQuery =
				"SELECT %USERFILTER% FROM %SCHEMA%.%USERTABLE%, %SCHEMA%.%USERCLAIMTABLE% WHERE [ClaimValue] = %CLAIMVALUE% AND [ClaimType] = %CLAIMTYPE%";
			GetUsersInRoleQuery =
				"SELECT %USERFILTER% FROM %SCHEMA%.%USERTABLE%, %SCHEMA%.%USERROLETABLE%, %SCHEMA%.%ROLETABLE% WHERE %SCHEMA%.%ROLETABLE%.[Name] = %ROLENAME% AND %SCHEMA%.%USERROLETABLE%.[RoleId] = %SCHEMA%.%ROLETABLE%.[Id] AND %SCHEMA%.%USERROLETABLE%.[UserId] = %SCHEMA%.%USERTABLE%.[Id]";
			IsInRoleQuery =
				"SELECT 1 FROM %SCHEMA%.%USERTABLE%, %SCHEMA%.%USERROLETABLE%, %SCHEMA%.%ROLETABLE% WHERE %SCHEMA%.%ROLETABLE%.[Name] = %ROLENAME% AND %SCHEMA%.%USERTABLE%.[Id] = %USERID% AND %SCHEMA%.%USERROLETABLE%.[RoleId] = %SCHEMA%.%ROLETABLE%.[Id] AND %SCHEMA%.%USERROLETABLE%.[UserId] = %SCHEMA%.%USERTABLE%.[Id]";
			RemoveClaimsQuery =
				"DELETE FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %ID% AND [ClaimType] = %CLAIMTYPE% AND [ClaimValue] = %CLAIMVALUE%";
			RemoveUserFromRoleQuery =
				"DELETE FROM %SCHEMA%.%USERROLETABLE% WHERE [UserId] = %USERID% AND [RoleId] = (SELECT [Id] FROM %SCHEMA%.%ROLETABLE% WHERE [Name] = %ROLENAME%)";
			RemoveLoginForUserQuery =
				"DELETE FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID% AND [LoginProvider] = %LOGINPROVIDER% AND [ProviderKey] = %PROVIDERKEY%";
			UpdateClaimForUserQuery =
				"UPDATE %SCHEMA%.%TABLENAME% SET [ClaimType] = %NEWCLAIMTYPE%, [ClaimValue] = %NEWCLAIMVALUE% WHERE [UserId] = %USERID% AND [ClaimType] = %CLAIMTYPE% AND [ClaimValue] = %CLAIMVALUE%";
			SelectClaimByRoleQuery =
				"SELECT %SCHEMA%.%ROLECLAIMTABLE%.* FROM %SCHEMA%.%ROLETABLE%, %SCHEMA%.%ROLECLAIMTABLE% WHERE [RoleId] = %ROLEID% AND %SCHEMA%.%ROLECLAIMTABLE%.[RoleId] = %SCHEMA%.%ROLETABLE%.[Id]";
			InsertRoleClaimQuery = "INSERT INTO %SCHEMA%.%TABLENAME% %COLUMNS% VALUES(%VALUES%)";
			DeleteRoleClaimQuery =
				"DELETE FROM %SCHEMA%.%TABLENAME% WHERE [RoleId] = %ROLEID% AND [ClaimType] = %CLAIMTYPE% AND [ClaimValue] = %CLAIMVALUE%";
			RoleTable = "[AspNetRoles]";
			UserTable = "[AspNetUsers]";
			UserClaimTable = "[AspNetUserClaims]";
			UserRoleTable = "[AspNetUserRoles]";
			UserLoginTable = "[AspNetUserLogins]";
			RoleClaimTable = "[AspNetRoleClaims]";

			#endregion Identity

			#region Resource Orders

			ResourceOrdersTable = "ResourceOrders";
			ResourceOrderFillsTable = "ResourceOrderFills";
			ResourceOrderItemsTable = "ResourceOrderItems";
			ResourceOrderSettingsTable = "ResourceOrderSettings";
			ResourceOrderFillUnitsTable = "ResourceOrderFillUnits";
			SelectAllOpenOrdersQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CloseDate] IS NULL";
			UpdateOrderFillStatusQuery = "UPDATE %SCHEMA%.%TABLENAME% %SETVALUES% WHERE [ResourceOrderFillId] = %ID%";
			SelectAllOpenNonDVisibleOrdersQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CloseDate] IS NULL AND [Visibility] = 0 AND [DepartmentId] != %DID% AND [NeededBy] > %DATE%";
			SelectAllItemsByOrderIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ResourceOrderId] = %ID%";
			SelectItemsByResourceOrderIdQuery = @"
					SELECT roi.*, rof.*
					FROM %SCHEMA%.%RESOURCEORDERITEMSTABLE% roi
					INNER JOIN %SCHEMA%.%RESOURCEORDERFILLSTABLE% rof ON roi.[ResourceOrderItemId] = rof.[ResourceOrderItemId]
					WHERE roi.[ResourceOrderId] = %RESOURCEORDERID%";
			SelectOrderFillUnitsByFillIdQuery = @"
					SELECT rofu.*, u.*
					FROM %SCHEMA%.%RESOURCEORDERFILLUNIT% rofu
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = rofu.[UnitId]
					WHERE rofu.[ResourceOrderFillId] = %FILLID%";
			#endregion Resource Orders

			#region Distribution Lists

			DistributionListsTable = "DistributionLists";
			DistributionListMembersTable = "DistributionListMembers";
			SelectDListByEmailQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [EmailAddress] = %EMAIL%";
			SelectAllEnabledDListsQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [IsDisabled] = 0";
			SelectDListMembersByListIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DistributionListId] = %LISTID%";
			SelectDListMembersByUserQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID%";
			SelectDListByIdQuery = @"
					SELECT dl.*, dlm.*
					FROM %SCHEMA%.%DISTRIBUTIONLISTSTABLE% dl
					LEFT JOIN %SCHEMA%.%DISTRIBUTIONLISTMEMBERSTABLE% dlm ON dlm.[DistributionListId] = dl.[DistributionListId]
					WHERE dl.[DistributionListId] = %LISTID%";
			SelectDListsByDIdQuery = @"
					SELECT dl.*, dlm.*
					FROM %SCHEMA%.%DISTRIBUTIONLISTSTABLE% dl
					LEFT JOIN %SCHEMA%.%DISTRIBUTIONLISTMEMBERSTABLE% dlm ON dlm.[DistributionListId] = dl.[DistributionListId]
					WHERE dl.[DepartmentId] = %DID%";

			#endregion Distribution Lists

			#region Custom States

			CustomStatesTable = "CustomStates";
			CustomStateDetailsTable = "CustomStateDetails";
			SelectStatesByDidUserQuery = @"
					SELECT %SCHEMA%.%CUSTOMSTATESTABLE%.*, %SCHEMA%.%CUSTOMSTATEDETAILSTABLE%.*
					FROM %SCHEMA%.%CUSTOMSTATESTABLE%
					LEFT JOIN %SCHEMA%.%CUSTOMSTATEDETAILSTABLE% ON %SCHEMA%.%CUSTOMSTATEDETAILSTABLE%.[CustomStateId] = %SCHEMA%.%CUSTOMSTATESTABLE%.[CustomStateId]
					WHERE [DepartmentId] = %DID%";
			SelectStatesByIdQuery = @"
					SELECT cs.*, csd.*
					FROM %SCHEMA%.%CUSTOMSTATESTABLE% cs
					LEFT JOIN %SCHEMA%.%MCUSTOMSTATEDETAILSTABLE% csd ON csd.[CustomStateId] = cs.[CustomStateId]
					WHERE cs.[CustomStateId] = %CUSTOMSTATEID%";

			#endregion Custom States

			#region Training

			TrainingsTable = "Trainings";
			TrainingAttachmentsTable = "TrainingAttachments";
			TrainingUsersTable = "TrainingUsers";
			TrainingQuestionsTable = "TrainingQuestions";
			TrainingQuestionAnswersTable = "TrainingQuestionAnswers";
			SelectTrainingUserByTandUIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [TrainingId] = %TRAININGID% AND [UserId] = %USERID%";
			SelectTrainingsByDIdQuery = @"
					SELECT t.*, tu.*
					FROM %SCHEMA%.%TRAININGSTABLE% t
					LEFT OUTER JOIN %SCHEMA%.%TRAININGUSERSTABLE% tu ON tu.[TrainingId] = t.[TrainingId]
					WHERE t.[DepartmentId] = %DID%";
			SelectTrainingQuestionsByTrainIdQuery = @"
					SELECT tq.*, tqa.*
					FROM %SCHEMA%.%TRAININGQUESTIONSTABLE% tq
					LEFT OUTER JOIN %SCHEMA%.%TRAININGQUESTIONANSWERSTABLE% tqa ON tqa.[TrainingQuestionId] = tq.[TrainingQuestionId]
					WHERE tq.[TrainingId] = %TRAININGID%";
			SelectTrainingByIdQuery = @"
					SELECT t.*, tu.*
					FROM %SCHEMA%.%TRAININGSTABLE% t
					LEFT OUTER JOIN %SCHEMA%.%TRAININGUSERSTABLE% tu ON tu.[TrainingId] = t.[TrainingId]
					WHERE t.[TrainingId] = %TRAININGID%";
			SelectTrainingAttachmentsBytIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [TrainingId] = %TRAININGID%";

			#endregion Training

			#region User Profile

			UserProfilesTable = "UserProfiles";
			SelectProfileByUserIdQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [UserId] = %USERID%";
			SelectProfileByMobileQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [MobileNumber] = %MOBILENUMBER%";
			SelectProfileByHomeQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [HomeNumber] = %HOMENUMBER%";
			SelectProfilesByIdsQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [UserId] IN (%USERIDS%)";
			SelectAllProfilesByDIdQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% ON %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId = %SCHEMA%.%USERPROFILESTABLE%.UserId
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [DepartmentId] = %DID%";
			SelectAllNonDeletedProfilesByDIdQuery = @"
					SELECT %SCHEMA%.%USERPROFILESTABLE%.*, %SCHEMA%.%ASPNETUSERSTABLE%.Email as 'MembershipEmail', %SCHEMA%.%ASPNETUSERSTABLE%.*
					FROM %SCHEMA%.%USERPROFILESTABLE%
					INNER JOIN %SCHEMA%.%DEPARTMENTMEMBERSTABLE% ON %SCHEMA%.%DEPARTMENTMEMBERSTABLE%.UserId = %SCHEMA%.%USERPROFILESTABLE%.UserId
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% ON %SCHEMA%.%ASPNETUSERSTABLE%.Id = %SCHEMA%.%USERPROFILESTABLE%.UserId
					WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0";

			#endregion User Profile

			#region Calendar

			CalendarItemsTable = "CalendarItems";
			CalendarItemAttendeesTable = "CalendarItemAttendees";
			CalendarItemTypesTable = "CalendarItemTypes";
			SelectCalendarItemByRecurrenceIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [RecurrenceId] = %ID%";
			DeleteCalendarItemQuery =
				"DELETE FROM %SCHEMA%.%TABLENAME% WHERE [CalendarItemId] = %ID% OR [RecurrenceId] = %ID%";
			SelectCalendarItemAttendeeByUserQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CalendarItemId] = %ID% AND [UserId] = %USERID%";
			SelectCalendarItemsByDateQuery = @"SELECT * FROM %SCHEMA%.%TABLENAME%
					WHERE [IsV2Schedule] = 1 AND [ReminderSent] = 0 AND [Reminder] > 0
					AND ([Start] >= @startDate OR ([RecurrenceType] > 0 AND ([RecurrenceEnd] IS NULL OR [RecurrenceEnd] > %STARTDATE%)))";
			SelectCalendarItemByIdQuery = @"
					SELECT ci.*, cia.*
					FROM %SCHEMA%.%CALENDARITEMSTABLE% ci
					LEFT OUTER JOIN %SCHEMA%.%CALITEMATTENDEESTABLE% cia ON cia.[CalendarItemId] = ci.[CalendarItemId]
					WHERE ci.[CalendarItemId] = %CALENDARITEMID%";
			SelectCalendarItemByDIdQuery = @"
					SELECT ci.*, cia.*
					FROM %SCHEMA%.%CALENDARITEMSTABLE% ci
					LEFT OUTER JOIN %SCHEMA%.%CALITEMATTENDEESTABLE% cia ON cia.[CalendarItemId] = ci.[CalendarItemId]
					WHERE ci.[DepartmentId] = %DID%";
			#endregion Calendar

			#region Logs

			LogsTable = "Logs";
			LogUsersTable = "LogUsers";
			LogUnitsTable = "LogUnits";
			CallLogsTable = "CallLogs";
			LogAttachmentsTable = "LogAttachments";
			SelectLogsByUserIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [LoggedByUserId] = %USERID%";
			SelectCallLogsByUserIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [LoggedByUserId] = %USERID%";
			SelectLogsByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectLogsByGroupIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [StationGroupId] = %GROUPID%";
			SelectCallLogsByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectLogUsersByLogIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [LogId] = %LOGID%";
			SelectLogUnitsByLogIdQuery = @"
					SELECT lu.*, u.*
					FROM %SCHEMA%.%LOGUNITSTABLE% lu
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = lu.[UnitId]
					WHERE lu.[LogId] = %LOGID%";
			SelectLogYearsByDeptQuery = @"
					SELECT DISTINCT YEAR(l.LoggedOn)
					FROM Logs l WHERE l.DepartmentId = %DID%
					ORDER BY 1 DESC";
			SelecAllLogsByDidYearQuery = @"
					SELECT * FROM %SCHEMA%.%TABLENAME%
					WHERE [DepartmentId] = %DID% AND year(LoggedOn) = %YEAR%
					ORDER BY LoggedOn DESC";

			#endregion Logs

			#region Units

			UnitsTable = "Units";
			UnitLogsTable = "UnitLogs";
			UnitRolesTable = "UnitRoles";
			UnitTypesTable = "UnitTypes";
			UnitStatesTable = "UnitStates";
			UnitStateRolesTable = "UnitStateRoles";
			UnitLocationsTable = "UnitLocations";
			UnitActiveRolesTable = "UnitActiveRoles";
			SelectUnitStatesByUnitIdQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[UnitId] = %UNITID%";
			SelectLastUnitStateByUnitIdQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[UnitId] = %UNITID%
					ORDER BY UnitStateId DESC";
			SelectLastUnitStateByUnitIdTimeQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[UnitId] = %UNITID% AND us.[UnitStateId] > %UNITSTATEID%
					ORDER BY Timestamp DESC";
			SelectUnitByDIdNameQuery = @"
					SELECT * FROM %SCHEMA%.%TABLENAME%
					WHERE [DepartmentId] = %DID% AND [Name] = %UNITNAME%";
			SelectUnitTypeByDIdNameQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [Type] = %TYPENAME%";
			SelectUnitLogsByUnitIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID% ORDER BY Timestamp DESC";
			SelectUnitRolesByUnitIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID%";
			SelectUnitsByGroupIdQuery = @"
					SELECT u.*, dg.*
					FROM [dbo].[Units] u
					INNER JOIN [dbo].[DepartmentGroups] dg ON dg.[DepartmentGroupId] = u.[StationGroupId]
					WHERE u.[StationGroupId] = %GROUPID%";
			SelectCurrentRolesByUnitIdQuery = @"
					SELECT * FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTATEROLESSTABLE% ON %SCHEMA%.%UNITSTATEROLESSTABLE%.[UnitStateId] = us.[UnitStateId]
					WHERE us.[UnitId] = @unitId AND [Timestamp] >= DATEADD(day,-2,GETUTCDATE())";
			SelectLatestUnitLocationByUnitId =
				"SELECT TOP 1 * FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID% ORDER BY UnitLocationId DESC";
			SelectLatestUnitLocationByUnitIdTimeQuery =
				"SELECT TOP 1 * FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID% AND [Timestamp] > %TIMESTAMP% ORDER BY UnitLocationId DESC";
			SelectUnitStatesByCallIdQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[DestinationId] = %CALLID%";
			SelectUnitByDIdTypeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [Type] = %TYPE%";
			SelectLastUnitStatesByDidQuery = @"
					SELECT  q.*, u.*
					FROM    (
							SELECT *, ROW_NUMBER() OVER (PARTITION BY UnitId ORDER BY UnitStateId DESC) us
							FROM UnitStates
							) q
					INNER JOIN Units u ON u.UnitId = q.UnitId
					WHERE u.DepartmentId = %DID% AND us = 1";
			SelectUnitStateByUnitStateIdQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[UnitStateId] = %UNITSTATEID%";
			SelectUnitActiveRolesByUnitIdQuery = @"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID%";
			DeleteUnitActiveRolesByUnitIdQuery = @"DELETE FROM %SCHEMA%.%TABLENAME% WHERE [UnitId] = %UNITID%";
			SelectActiveRolesForUnitsByDidQuery = @"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID%";
			SelectUnitsByDIdQuery = @"
					SELECT u.*, ur.*
					FROM [dbo].[Units] u
					LEFT JOIN [dbo].[UnitRoles] ur ON ur.[UnitId] = u.[UnitId]
					WHERE u.[DepartmentId] = %DID%";

			#endregion Units

			#region Shifts

			ShiftsTable = "Shifts";
			ShiftPersonsTable = "ShiftPersons";
			ShiftDaysTable = "ShiftDays";
			ShiftGroupsTable = "ShiftGroups";
			ShiftSignupsTable = "ShiftSignups";
			ShiftSignupTradesTable = "ShiftSignupTrades";
			ShiftSignupTradeUsersTable = "ShiftSignupTradeUsers";
			ShiftSignupTradeUserShiftsTable = "ShiftSignupTradeUserShifts";
			ShiftStaffingsTable = "ShiftStaffings";
			ShiftStaffingPersonsTable = "ShiftStaffingPersons";
			ShiftGroupRolesTable = "ShiftGroupRoles";
			ShiftGroupAssignmentsTable = "ShiftGroupAssignments";
			SelectShiftStaffingByDayQuery = @"
					SELECT %SCHEMA%.%SHIFTSTAFFINGSTABLE%.*, %SCHEMA%.%SHIFTSTAFFINGPERSONSTABLE%.*
					FROM %SCHEMA%.%SHIFTSTAFFINGSTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTSTAFFINGPERSONSTABLE% ON %SCHEMA%.%SHIFTSTAFFINGPERSONSTABLE%.[ShiftStaffingId] =  %SCHEMA%.%SHIFTSTAFFINGSTABLE%.[ShiftStaffingId]
					WHERE [ShiftId] = %SHIFTID% AND [ShiftDay] = %SHIFTDAY%";
			SelectShiftGroupByGroupQuery = @"
					SELECT %SCHEMA%.%SHIFTGROUPSTABLE%.*, %SCHEMA%.%SHIFTGROUPROLESTABLE%.*
					FROM %SCHEMA%.%SHIFTGROUPSTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTGROUPROLESTABLE% ON %SCHEMA%.%SHIFTGROUPROLESTABLE%.[ShiftGroupId] =  %SCHEMA%.%SHIFTGROUPSTABLE%.[ShiftGroupId]
					WHERE [DepartmentGroupId] = %GROUPID%";
			SelectShiftAssignmentByGroupQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ShiftGroupId] = %SHIFTGROUPID%";
			SelectShiftSignupTradeUsersByTradeIdQuery = @"
					SELECT %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.*, %SCHEMA%.%SHIFTSIGNUPTRADEUSERSHIFTSTABLE%.*
					FROM %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTSIGNUPTRADEUSERSHIFTSTABLE% ON %SCHEMA%.%SHIFTSIGNUPTRADEUSERSHIFTSTABLE%.[ShiftSignupTradeUserId] =  %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.[ShiftSignupTradeUserId]
					WHERE [ShiftSignupTradeId] = %SHIFTSIGNUPTRADEID%";
			SelectShiftSignupByShiftIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ShiftId] = %SHIFTID%";
			SelectShiftSignupTradeBySourceIdQuery = @"
					SELECT %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.*, %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.*
					FROM %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE% ON %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.[ShiftSignupTradeId] =  %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.[ShiftSignupTradeId]
					WHERE [SourceShiftSignupId] = %SHIFTSIGNUPID%";
			SelectShiftSignupTradeByTargetIdQuery = @"
					SELECT %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.*, %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.*
					FROM %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE% ON %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.[ShiftSignupTradeId] =  %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.[ShiftSignupTradeId]
					WHERE [TargetShiftSignupId] = %SHIFTSIGNUPID%";
			SelectShiftDaysByShiftIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ShiftId] = %SHIFTID%";
			SelectShiftAndDaysByShiftIdQuery = @"
					SELECT s.*, sd.*
					FROM %SCHEMA%.%SHIFTSTABLE% s
					LEFT JOIN %SCHEMA%.%SHIFTDAYSTABLE% sd ON sd.[ShiftId] = s.[ShiftId]
					WHERE s.[ShiftId] = %SHIFTID%";
			SelectShiftAndDaysQuery = @"
					SELECT %SCHEMA%.%SHIFTSTABLE%.*, %SCHEMA%.%SHIFTDAYSTABLE%.*
					FROM %SCHEMA%.%SHIFTSTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTDAYSTABLE% ON %SCHEMA%.%SHIFTDAYSTABLE%.[ShiftId] = %SCHEMA%.%SHIFTSTABLE%.[ShiftId]";
			SelectShiftAndDaysJSONQuery = @"
					SELECT (SELECT *,
				      JSON_QUERY((SELECT *
				         FROM [dbo].[Departments]
						 WHERE [dbo].[Departments].DepartmentId = sh.DepartmentId
				       FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Department',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = sg.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'DepartmentGroup',
							 (SELECT *
								 FROM [dbo].[ShiftGroupRoles]
								 WHERE [dbo].[ShiftGroupRoles].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Roles',
							  (SELECT *
								 FROM [dbo].[ShiftGroupAssignments]
								 WHERE [dbo].[ShiftGroupAssignments].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Assignments'
				         FROM [dbo].[ShiftGroups] sg
						 WHERE sg.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Groups',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftDays] sd
						 WHERE sd.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Days',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftPersons] sp
						 WHERE sp.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Personnel',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftAdmins] sa
						 WHERE sa.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Admins',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							 JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = ss.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Group'
				         FROM [dbo].[ShiftSignups] ss
						 WHERE ss.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Signups'

				FROM [dbo].[Shifts] sh
				FOR JSON PATH) AS 'JsonResult'";
			SelectShiftSignupByUserIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID%";
			SelectShiftSignupTradeByUserIdQuery = @"
					SELECT %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.*, %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.*
					FROM %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%
					LEFT JOIN %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE% ON %SCHEMA%.%SHIFTSIGNUPTRADEUSERSTABLE%.[ShiftSignupTradeId] =  %SCHEMA%.%SHIFTSIGNUPTRADESTABLE%.[ShiftSignupTradeId]
					WHERE [UserId] = %USERID%";
			SelectOpenShiftSignupTradesByUserIdQuery = @"
					SELECT *
					FROM ShiftSignupTrades sst
					INNER JOIN ShiftSignupTradeUsers sstu ON sstu.ShiftSignupTradeId = sst.ShiftSignupTradeId
					WHERE sst.UserId = %USERID% AND sst.UserId != %USERID% AND sst.TargetShiftSignupId IS NULL";
			SelectShiftAndDaysByDIdQuery = @"
					SELECT s.*, sd.*
					FROM %SCHEMA%.%SHIFTSTABLE% s
					LEFT JOIN %SCHEMA%.%SHIFTDAYSTABLE% sd ON sd.[ShiftId] = s.[ShiftId]
					WHERE s.[DepartmentId] = %DID%";
			SelectShiftByShiftIdQuery = @"
					SELECT s.*, sp.*
					FROM %SCHEMA%.%SHIFTSTABLE% s
					LEFT JOIN %SCHEMA%.%SHIFTPERSONSTABLE% sp ON sp.[ShiftId] = s.[ShiftId]
					WHERE s.[ShiftId] = %SHIFTID%";
			SelectShiftPersonByShiftIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ShiftId] = %SHIFTID%";
			SelectShiftGroupByShiftIdQuery = @"
					SELECT sg.*, sgr.*
					FROM %SCHEMA%.%SHIFTGROUPSTABLE% sg
					LEFT JOIN %SCHEMA%.%SHIFTGROUPROLESTABLE% sgr ON sgr.[ShiftGroupId] = sg.[ShiftGroupId]
					WHERE sg.[ShiftId] = %SHIFTID%";
			SelectShiftDayByIdQuery = @"
					SELECT sd.*, s.*
					FROM %SCHEMA%.%SHIFTDAYSTABLE% sd
					INNER JOIN %SCHEMA%.%SHIFTSTABLE% s ON s.[ShiftId] = sd.[ShiftId]
					WHERE sd.[ShiftDayId] = %SHIFTDAYID%";
			SelectShiftSignupByShiftIdDateQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [ShiftId] = %SHIFTID% AND CAST([ShiftDay] AS DATE) = CAST(%SHIFTDAYDATE% AS DATE)";
			SelectShiftGroupRolesByGroupIdQuery = @"
					SELECT sgr.*, pr.*
					FROM %SCHEMA%.%SHIFTGROUPROLESTABLE% sgr
					INNER JOIN %SCHEMA%.%PERSONNELROLESTABLE% pr ON pr.[PersonnelRoleId] = sgr.[PersonnelRoleId]
					WHERE sgr.[ShiftGroupId] = %SHIFTGROUPID%";
			SelectShiftTradeAndSourceByUserIdQuery = @"
					SELECT st.*, ss.*
					FROM %SCHEMA%.%SHIFTTRADESTABLE% st
					INNER JOIN %SCHEMA%.%SHIFTSIGNUPSTABLE% ss ON ss.[ShiftSignupId] = st.[SourceShiftSignupId]
					WHERE st.[UserId] = %USERID%";
			SelectShiftByShiftIdJSONQuery = @"
					SELECT (SELECT *,
				      JSON_QUERY((SELECT *
				         FROM [dbo].[Departments]
						 WHERE [dbo].[Departments].DepartmentId = sh.DepartmentId
				       FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Department',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = sg.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'DepartmentGroup',
							 (SELECT *
								 FROM [dbo].[ShiftGroupRoles]
								 WHERE [dbo].[ShiftGroupRoles].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Roles',
							  (SELECT *
								 FROM [dbo].[ShiftGroupAssignments]
								 WHERE [dbo].[ShiftGroupAssignments].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Assignments'
				         FROM [dbo].[ShiftGroups] sg
						 WHERE sg.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Groups',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftDays] sd
						 WHERE sd.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Days',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftPersons] sp
						 WHERE sp.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Personnel',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftAdmins] sa
						 WHERE sa.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Admins',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							   JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = ss.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Group'
				         FROM [dbo].[ShiftSignups] ss
						 WHERE ss.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Signups'

				FROM [dbo].[Shifts] sh
					WHERE sh.[ShiftId] = %SHIFTID%
					FOR JSON PATH) AS 'JsonResult'";
			SelectShiftsByDidJSONQuery = @"
					SELECT (SELECT *,
				      JSON_QUERY((SELECT *
				         FROM [dbo].[Departments]
						 WHERE [dbo].[Departments].DepartmentId = sh.DepartmentId
				       FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Department',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = sg.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'DepartmentGroup',
							 (SELECT *
								 FROM [dbo].[ShiftGroupRoles]
								 WHERE [dbo].[ShiftGroupRoles].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Roles',
							  (SELECT *
								 FROM [dbo].[ShiftGroupAssignments]
								 WHERE [dbo].[ShiftGroupAssignments].ShiftGroupId = sg.ShiftGroupId
							   FOR JSON PATH) AS 'Assignments'
				         FROM [dbo].[ShiftGroups] sg
						 WHERE sg.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Groups',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftDays] sd
						 WHERE sd.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Days',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftPersons] sp
						 WHERE sp.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Personnel',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift'
				         FROM [dbo].[ShiftAdmins] sa
						 WHERE sa.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Admins',
					   (SELECT *,
							JSON_QUERY((SELECT *
								 FROM [dbo].[Shifts]
								 WHERE [dbo].[Shifts].ShiftId = sh.ShiftId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Shift',
							 JSON_QUERY((SELECT *
								 FROM [dbo].[DepartmentGroups]
								 WHERE [dbo].[DepartmentGroups].DepartmentGroupId = ss.DepartmentGroupId
							   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS 'Group'
				         FROM [dbo].[ShiftSignups] ss
						 WHERE ss.ShiftId = sh.ShiftId
				       FOR JSON PATH) AS 'Signups'

				FROM [dbo].[Shifts] sh
				WHERE sh.DepartmentId = %DID%
				FOR JSON PATH) AS 'JsonResult'";
			SelectShiftSignupsByGroupIdAndDateQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentGroupId] = %GROUPID% AND CAST([ShiftDay] AS DATE) = CAST(%SHIFTDAYDATE% AS DATE)";

			#endregion Shifts

			#region Dispatch Protocols

			DispatchProtocolsTable = "DispatchProtocols";
			DispatchProtocolTriggersTable = "DispatchProtocolTriggers";
			DispatchProtocolAttachmentsTable = "DispatchProtocolAttachments";
			DispatchProtocolQuestionsTable = "DispatchProtocolQuestions";
			DispatchProtocolQuestionAnswersTable = "DispatchProtocolQuestionAnswers";
			SelectProtocolByIdQuery = @"
					SELECT p.*, pt.*
					FROM %SCHEMA%.%PROTOCOLSTABLE% p
					LEFT OUTER JOIN %SCHEMA%.%PROTOCOLTRIGGERSSTABLE% pt ON pt.[DispatchProtocolId] = p.[DispatchProtocolId]
					WHERE p.[DispatchProtocolId] = %PROTOCOLID%";
			SelectProtocolsByDIdQuery = @"
					SELECT p.*, pt.*
					FROM %SCHEMA%.%PROTOCOLSTABLE% p
					LEFT OUTER JOIN %SCHEMA%.%PROTOCOLTRIGGERSSTABLE% pt ON pt.[DispatchProtocolId] = p.[DispatchProtocolId]
					WHERE p.[DepartmentId] = %DID%";
			SelectProtocolQuestionsByProIdQuery = @"
					SELECT pq.*, pqa.*
					FROM %SCHEMA%.%PROTOCOLQUESTIONSTABLE% pq
					LEFT OUTER JOIN %SCHEMA%.%PROTOCOLQUESTIONANSWERSTABLE% pqa ON pqa.[DispatchProtocolQuestionId] = pq.[DispatchProtocolQuestionId]
					WHERE pq.[DispatchProtocolId] = %PROTOCOLID%";
			SelectProtocolAttachmentsByProIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DispatchProtocolId] = %PROTOCOLID%";
			SelectProtocolTriggersByProIdQuery = @"
					SELECT *
					FROM %SCHEMA%.%PROTOCOLTRIGGERSTABLE% pt
					WHERE pt.[DispatchProtocolId] = %PROTOCOLID%";

			#endregion Dispatch Protocols

			#region Calls

			CallsTable = "Calls";
			CallDispatchesTable = "CallDispatches";
			CallDispatchGroupsTable = "CallDispatchGroups";
			CallDispatchUnitsTable = "CallDispatchUnits";
			CallDispatchRolesTable = "CallDispatchRoles";
			CallTypesTable = "CallTypes";
			CallNotesTable = "CallNotes";
			CallAttachmentsTable = "CallAttachments";
			DepartmentCallPrioritiesTable = "DepartmentCallPriorities";
			CallProtocolsTable = "CallProtocols";
			SelectAllCallsByDidDateQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0 AND [LoggedOn] >= %STARTDATE% AND [LoggedOn] <= %ENDDATE%";
			SelectAllClosedCallsByDidDateQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0 AND [State] > 0";
			SelectAllCallDispatchesByGroupIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentGroupId] = %GROUPID%";
			SelectCallAttachmentByCallIdTypeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID% AND [CallAttachmentType] = %TYPE%";
			SelectAllOpenCallsByDidDateQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0 AND [State] = 0";
			SelectAllCallsByDidLoggedOnQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND AND [LoggedOn] >= %DATE%";
			UpdateUserDispatchesAsSentQuery = @"
					UPDATE Calls
					SET DispatchCount = (DispatchCount + 1),
						  LastDispatchedOn = GETUTCDATE()
					WHERE CallId = %CALLID%

					UPDATE CallDispatches
					SET DispatchCount = (DispatchCount + 1),
						  LastDispatchedOn = GETUTCDATE()
					WHERE CallId = %CALLID% AND %USERIDS% LIKE '%|' +convert(varchar(max), UserId) + '|%'";
			SelectCallProtocolsByCallIdQuery = @"
					SELECT %SCHEMA%.%CALLPROTOCOLSTABLE%.*, %SCHEMA%.%DISPATCHPROTOCOLSTABLE%.*
					FROM  %SCHEMA%.%CALLPROTOCOLSTABLE%
					INNER JOIN %SCHEMA%.%DISPATCHPROTOCOLSTABLE% ON %SCHEMA%.%DISPATCHPROTOCOLSTABLE%.[DispatchProtocolId] = %SCHEMA%.%CALLPROTOCOLSTABLE%.[DispatchProtocolId]
					WHERE [CallId] = %CALLID%";
			SelectAllCallDispatchesByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectCallAttachmentByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectAllCallGroupDispsByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectAllCallUnitDispsByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectAllCallRoleDispsByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectCallNotesByCallIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [CallId] = %CALLID%";
			SelectCallYearsByDeptQuery = @"
					SELECT DISTINCT YEAR(c.LoggedOn)
					FROM Calls c WHERE c.DepartmentId = %DID%
					ORDER BY 1 DESC";
			SelectAllClosedCallsByDidYearQuery = @"
					SELECT * FROM %SCHEMA%.%TABLENAME%
					WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0 AND [State] > 0 AND year(LoggedOn) = %YEAR%
					ORDER BY LoggedOn DESC";
			SelectNonDispatchedScheduledCallsByDateQuery = @"
					SELECT *
					FROM %SCHEMA%.%TABLENAME%
					WHERE [HasBeenDispatched] = 0 AND [IsDeleted] = 0 AND [DispatchOn] IS NOT NULL AND [DispatchOn] >= %STARTDATE% AND [DispatchOn] <= %ENDDATE%";
			SelectNonDispatchedScheduledCallsByDidQuery = @"
					SELECT *
					FROM %SCHEMA%.%TABLENAME%
					WHERE [HasBeenDispatched] = 0 AND [IsDeleted] = 0 AND [DepartmentId] = %DID%";

			#endregion Calls

			#region Department Groups

			DepartmentGroupsTable = "DepartmentGroups";
			DepartmentGroupMembersTable = "DepartmentGroupMembers";
			//SelectGroupMembersByGroupIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentGroupId] = %GROUPID%";

			SelectGroupMembersByGroupIdQuery = @"
					SELECT dgm.*
					FROM [dbo].DepartmentGroupMembers dgm
					INNER JOIN [dbo].DepartmentGroups dg ON dg.[DepartmentGroupId] =  dgm.[DepartmentGroupId]
					INNER JOIN [dbo].DepartmentMembers dm ON dm.UserId = dgm.UserId AND dm.[DepartmentId] = dg.[DepartmentId]
					WHERE dgm.[DepartmentGroupId] = %GROUPID% AND dm.[IsDisabled] = 0 AND dm.[IsDeleted] = 0";

			SelectGroupMembersByUserDidQuery = @"
					SELECT dgm.*, dg.*
					FROM %SCHEMA%.%GROUPMEMBERSSTABLE% dgm
					INNER JOIN %SCHEMA%.%GROUPSTABLE% dg ON dg.[DepartmentGroupId] =  dgm.[DepartmentGroupId]
					WHERE dgm.[UserId] = %USERID% AND dgm.[DepartmentId] = %DID%";
			SelectAllGroupsByDidQuery = @"
					SELECT dg.*, dgm.*
					FROM %SCHEMA%.%GROUPSTABLE% dg
					LEFT JOIN (SELECT dgm1.*
						FROM %SCHEMA%.%GROUPMEMBERSSTABLE% dgm1
						INNER JOIN %SCHEMA%.%DEPARTMENTMEMBERSSTABLE% dm ON dgm1.[UserId] = dm.[UserId]
						WHERE dm.[IsDeleted] = 0) AS dgm ON dgm.[DepartmentGroupId] = dg.[DepartmentGroupId]
					WHERE dg.[DepartmentId] = %DID%";
			SelectAllGroupsByParentIdQuery = @"
					SELECT %SCHEMA%.%GROUPSTABLE%.*, %SCHEMA%.%GROUPMEMBERSSTABLE%.*
					FROM %SCHEMA%.%GROUPSTABLE%
					LEFT JOIN %SCHEMA%.%GROUPMEMBERSSTABLE% ON %SCHEMA%.%GROUPMEMBERSSTABLE%.[DepartmentGroupId] =  %SCHEMA%.%GROUPSTABLE%.[DepartmentGroupId]
					WHERE [ParentDepartmentGroupId] = %GROUPID%";
			SelectGroupByDispatchCodeQuery = @"
					SELECT %SCHEMA%.%GROUPSTABLE%.*, %SCHEMA%.%GROUPMEMBERSSTABLE%.*
					FROM %SCHEMA%.%GROUPSTABLE%
					LEFT JOIN %SCHEMA%.%GROUPMEMBERSSTABLE% ON %SCHEMA%.%GROUPMEMBERSSTABLE%.[DepartmentGroupId] =  %SCHEMA%.%GROUPSTABLE%.[DepartmentGroupId]
					WHERE [DispatchEmail] = %CODE%";
			SelectGroupByMessageCodeQuery = @"
					SELECT %SCHEMA%.%GROUPSTABLE%.*, %SCHEMA%.%GROUPMEMBERSSTABLE%.*
					FROM %SCHEMA%.%GROUPSTABLE%
					LEFT JOIN %SCHEMA%.%GROUPMEMBERSSTABLE% ON %SCHEMA%.%GROUPMEMBERSSTABLE%.[DepartmentGroupId] =  %SCHEMA%.%GROUPSTABLE%.[DepartmentGroupId]
					WHERE [MessageEmail] = %CODE%";
			SelectGroupByGroupIdQuery = @"
					SELECT dg.*, dgm.*
					FROM %SCHEMA%.%GROUPSTABLE% dg
					LEFT JOIN %SCHEMA%.%GROUPMEMBERSSTABLE% dgm ON dgm.[DepartmentGroupId] = dg.[DepartmentGroupId]
					WHERE dg.[DepartmentGroupId] = %GROUPID%";
			DeleteGroupMembersByGroupIdDidQuery = @"
					DELETE FROM %SCHEMA%.%TABLENAME%
					WHERE DepartmentId = %DID% AND DepartmentGroupId = %ID%";
			#endregion Department Groups

			#region Payments

			PaymentsTable = "Payments";
			SelectGetDepartmentPlanCountsQuery = @"
					SELECT
					(SELECT COUNT(*) FROM DepartmentMembers dm WHERE dm.DepartmentId = %DID% AND IsDisabled = 0 AND IsDeleted = 0) AS 'UsersCount',
					(SELECT COUNT(*) FROM DepartmentGroups dg WHERE dg.DepartmentId = %DID%) AS 'GroupsCount',
					(SELECT COUNT(*) FROM Units u WHERE u.DepartmentId = %DID%) AS 'UnitsCount'";
			SelectPaymentByTransactionIdQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [TransactionId] = %TRANSACTIONID%";
			SelectPaymentsByDIdQuery = @"
					SELECT pa.*, pl.*
					FROM %SCHEMA%.%PAYMENTSTSTABLE% pa
					LEFT JOIN %SCHEMA%.%PLANSTABLE% pl ON pl.[PlanId] = pa.[PlanId]
					WHERE pa.[DepartmentId] = %DID%";
			SelectPaymentByIdQuery = @"
					SELECT pa.*, pl.*
					FROM %SCHEMA%.%PAYMENTSTSTABLE% pa
					LEFT JOIN %SCHEMA%.%PLANSTABLE% pl ON pl.[PlanId] = pa.[PlanId]
					WHERE pa.[PaymentId] = %PAYMENTID%";

			#endregion Payments

			#region User States

			UserStatesTable = "UserStates";
			SelectLatestUserStatesByDidQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [Timestamp] >= %TIMESTAMP%";
			SelectUserStatesByUserIdQuery = "SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID%";
			SelectLastUserStatesByUserIdQuery =
				"SELECT TOP 1 * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID% ORDER BY UserStateId DESC";
			SelectPreviousUserStatesByUserIdQuery =
				"SELECT TOP 1 * FROM %SCHEMA%.%TABLENAME% WHERE [UserId] = %USERID% AND [UserStateId] < %USERSTATEID% ORDER BY UserStateId DESC";
			SelectUserStatesByDIdDateRangeQuery =
				"SELECT * FROM %SCHEMA%.%TABLENAME% WHERE [DepartmentId] = %DID% AND [Timestamp] >= %STARTDATE% AND [Timestamp] <= %ENDDATE%";

			#endregion User States

			#region Plans

			PlansTable = "Plans";
			PlanLimitsTable = "PlanLimits";
			SelectPlanByPlanIdQuery = @"
					SELECT p.*, pl.*
					FROM %SCHEMA%.%PLANSTABLE% p
					INNER JOIN %SCHEMA%.%PLANLIMITS% pl ON pl.[PlanId] = p.[PlanId]
					WHERE p.[PlanId] = %PLANID%";

			#endregion Plans

			#region Mapping

			PoisTableName = "Pois";
			POITypesTableName = "POITypes";
			SelectPoiTypesByDIdQuery = @"
					SELECT pt.*, p.*
					FROM %SCHEMA%.%POITYPESTABLE% pt
					LEFT JOIN %SCHEMA%.%POISTABLE% p ON pt.[PoiTypeId] = p.[PoiTypeId]
					WHERE pt.[DepartmentId] = %DID%";
			SelectPoiTypeByIdQuery = @"
					SELECT pt.*, p.*
					FROM %SCHEMA%.%POITYPESTABLE% pt
					LEFT JOIN %SCHEMA%.%POISTABLE% p ON pt.[PoiTypeId] = p.[PoiTypeId]
					WHERE pt.[PoiTypeId] = %POITYPEID%";

			#endregion Mapping

			#region Notes

			NotesTableName = "Notes";
			SelectNotesByDIdQuery = @"
					SELECT n.*, d.*
					FROM %SCHEMA%.%NOTESTABLE% n
					INNER JOIN %SCHEMA%.%DEPARTMENTSTABLE% d ON d.[DepartmentId] = n.[DepartmentId]
					WHERE n.[DepartmentId] = %DID%";

			#endregion Notes

			#region Forms

			FormsTable = "Forms";
			FormAutomationsTable = "FormAutomations";
			SelectFormByIdQuery = @"
					SELECT f.*, fa.*
					FROM %SCHEMA%.%FORMSTABLE% f
					LEFT OUTER JOIN %SCHEMA%.%FORMAUTOMATIONSTABLE% fa ON fa.[FormId] = f.[FormId]
					WHERE f.[FormId] = %FORMID%";
			SelectFormsByDIdQuery = @"
					SELECT f.*, fa.*
					FROM %SCHEMA%.%FORMSTABLE% f
					LEFT OUTER JOIN %SCHEMA%.%FORMAUTOMATIONSTABLE% fa ON fa.[FormId] = f.[FormId]
					WHERE f.[DepartmentId] = %DID% AND [IsDeleted] = 0";
			SelectFormAutomationsByFormIdQuery = @"
					SELECT fa.*
					FROM %SCHEMA%.%FORMAUTOMATIONSTABLE% fa
					WHERE fa.[FormId] = %FORMID%";
			SelectNonDeletedFormsByDIdQuery = @"
					SELECT f.*, fa.*
					FROM %SCHEMA%.%FORMSTABLE% f
					LEFT OUTER JOIN %SCHEMA%.%FORMAUTOMATIONSTABLE% fa ON fa.[FormId] = f.[FormId]
					WHERE f.[IsDeleted] = 0 AND f.[DepartmentId] = %DID%";
			UpdateFormsToEnableQuery = @"
					UPDATE Forms
					SET IsActive = 1
					WHERE FormId = %FORMID%";
			UpdateFormsToDisableQuery = @"
					UPDATE Forms
					SET IsActive = 0
					WHERE FormId = %FORMID%";

			#endregion Forms

			#region Voice

			DepartmentVoiceTableName = "DepartmentVoices";
			DepartmentVoiceChannelsTableName = "DepartmentVoiceChannels";
			DepartmentVoiceUsersTableName = "DepartmentVoiceUsers";
			SelectVoiceByDIdQuery = @"
					SELECT dv.*, dvc.*
					FROM %SCHEMA%.%DEPARTMENTVOICETABLE% dv
					LEFT OUTER JOIN %SCHEMA%.%DEPARTMENTVOICECHANNELSTABLE% dvc ON dv.DepartmentVoiceId = dvc.DepartmentVoiceId
					WHERE dv.[DepartmentId] = %DID%";
			SelectVoiceChannelsByVoiceIdQuery = @"
					SELECT dvc.*
					FROM %SCHEMA%.%DEPARTMENTVOICECHANNELSTABLE% dvc
					WHERE dvc.[DepartmentVoiceId] = %VOICEID%";
			SelectVoiceUserByUserIdQuery = @"
					SELECT dvu.*
					FROM %SCHEMA%.%DEPARTMENTVOICEUSERSSTABLE% dvu
					WHERE dvu.[UserId] = %USERID%";
			SelectVoiceChannelsByDIdQuery = @"
					SELECT dvc.*
					FROM %SCHEMA%.%DEPARTMENTVOICECHANNELSTABLE% dvc
					WHERE dvc.[DepartmentId] = %DID%";

			#endregion Voice

			#region Unit States
			SelectUnitStatesByUnitInDateRangeQuery = @"
					SELECT us.*, u.*
					FROM %SCHEMA%.%UNITSTATESTABLE% us
					INNER JOIN %SCHEMA%.%UNITSTABLE% u ON u.[UnitId] = us.[UnitId]
					WHERE us.[UnitId] = %UNITID% AND us.[Timestamp] >= %STARTDATE% AND us.[Timestamp] <= %ENDDATE%";
			#endregion Unit States

			#region Workshifts
			WorkshiftsTable = "Workshifts";
			WorkshiftDaysTable = "WorkshiftDays";
			WorkshiftEntitiesTable = "WorkshiftEntities";
			WorkshiftFillsTable = "WorkshiftFills";
			SelectAllWorkshiftsAndDaysByDidQuery = @"
					SELECT ws.*, wsd.*
					FROM %SCHEMA%.%WORKSHIFTSTABLE% ws
					LEFT OUTER JOIN %SCHEMA%.%WORKSHIFTDAYSTABLE% wsd ON ws.WorkshiftId = wsd.WorkshiftId
					WHERE ws.[DepartmentId] = %DID%";
			SelectWorkshiftByIdQuery = @"
					SELECT ws.*, wsd.*
					FROM %SCHEMA%.%WORKSHIFTSTABLE% ws
					LEFT OUTER JOIN %SCHEMA%.%WORKSHIFTDAYSTABLE% wsd ON ws.WorkshiftId = wsd.WorkshiftId
					WHERE ws.[WorkshiftId] = %ID%";
			SelectWorkshiftEntitiesByWorkshiftIdQuery = @"
					SELECT *
					FROM %SCHEMA%.%WORKSHIFTENTITIESTABLE%
					WHERE [WorkshiftId] = %ID%";
			SelectWorkshiftFillsByWorkshiftIdQuery = @"
					SELECT *
					FROM %SCHEMA%.%WORKSHIFTFILLSTABLE%
					WHERE [WorkshiftId] = %ID%";
			#endregion Workshifts

			#region CallReferences
			CallReferencesTable = "CallReferences";
			SelectAllCallReferencesBySourceCallIdQuery = @"
					SELECT cr.*, c.*
					FROM %SCHEMA%.%CALLREFERENCESTABLE% cr
					INNER JOIN %SCHEMA%.%CALLSTABLE% c ON cr.TargetCallId = c.CallId
					WHERE cr.[SourceCallId] = %CALLID%";
			SelectAllCallReferencesByTargetCallIdQuery = @"
					SELECT cr.*, c.*
					FROM %SCHEMA%.%CALLREFERENCESTABLE% cr
					INNER JOIN %SCHEMA%.%CALLSTABLE% c ON cr.SourceCallId = c.CallId
					WHERE cr.[TargetCallId] = %CALLID%";
			#endregion CallReferences

			#region Scheduled Tasks

			ScheduledTasksTable = "ScheduledTasks";
			SelectAllUpcomingOrRecurringReportTasksQuery = @"
					SELECT st.*, d.TimeZone as 'DepartmentTimeZone', u.Email as 'UserEmailAddress'
					FROM %SCHEMA%.%SCHEDULEDTASKSTABLE% st
					INNER JOIN %SCHEMA%.%DEPARTMENTSTABLE% d ON st.DepartmentId = d.DepartmentId
					INNER JOIN %SCHEMA%.%ASPNETUSERSTABLE% u ON st.UserId = u.Id
					WHERE st.[Active] = 1 AND st.[TaskType] = 3 AND (st.[SpecifcDate] IS NULL OR st.[SpecifcDate] > %DATETIME%) AND st.[DepartmentId] != 0
			";

			#endregion Scheduled Tasks
		}
	}
}
