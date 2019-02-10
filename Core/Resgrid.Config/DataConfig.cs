namespace Resgrid.Config
{
	public class DataConfig
	{
		public static string ConnectionString = "Data Source=(local);Initial Catalog=Resgrid;Integrated Security=True;MultipleActiveResultSets=True;";

		public const string UsersIdentityRoleId = "38b461d7-e848-46ef-8c06-ece5b618d9d1";
		public const string AdminsIdentityRoleId = "1f6a03a8-62f4-4179-80fc-2eb96266cf04";
		public const string AffiliatesIdentityRoleId = "3aba8863-e46d-40cc-ab86-309f9c3e4f97";
			   
		public const string SystemTestUser1Id = "474468cd-6bfd-4717-8302-60bbe3530fdb";
		public const string SystemTestUser1Username = "TestAccount1";
		public const string SystemTestUser1Email = "testaccount1@yourcompany.local";
		public const string SystemTestUser1PasswordHash = "AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==";
		public const string SystemTestUser1SecurityStamp = "afa823df-6084-4a43-9356-997450d84bb0";
			   
		public const string SystemTestUser2Id = "c4d78e63-aa6e-4c38-9f03-a0a6311b4aa5";
		public const string SystemTestUser2Username = "TestAccount2";
		public const string SystemTestUser2Email = "testaccount2@yourcompany.local";
		public const string SystemTestUser2PasswordHash = "AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==";
		public const string SystemTestUser2SecurityStamp = "80cabb04-9435-4b52-8f5c-b59d9d551f5c";
			   
		public const string SystemAdminUserId = "66352698-a346-4a99-bb9a-12ac35aad62e";
		public const string SystemAdminUserUsername = "Administrator";
		public const string SystemAdminUserEmail = "administrator@yourcompany.local";
		public const string SystemAdminUserPasswordHash = "AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==";
		public const string SystemAdminUserSecurityStamp = "39fef507-3dd3-4c78-b8d2-d59e5b1f963e";

		#region SQL Statements
		public const string TestDepartmentSql = @"/* SYSTEM ONLY DEPARTMENT, USED FOR HEALTH CHECKS AND SETTING UP SHARED DATA */

DECLARE @TestDepartmentId INT
SET @TestDepartmentId = 1

DECLARE @TestDepartmentListId INT

INSERT INTO [dbo].[Applications]
           ([ApplicationId]
           ,[ApplicationName]
           ,[Description])
     VALUES
           ('97a8aef2-6330-4e56-a81c-d359bca614b0'
           ,'Resgrid'
           ,'Resgrid App')

INSERT INTO [dbo].[Users]
           ([UserId]
           ,[ApplicationId]
           ,[UserName]
           ,[IsAnonymous]
           ,[LastActivityDate])
     VALUES
           ('474468cd-6bfd-4717-8302-60bbe3530fdb'
		   ,'97a8aef2-6330-4e56-a81c-d359bca614b0'
           ,'systematest1'
           ,0
		   ,'1753-01-01 00:00:00.000')


INSERT INTO [dbo].[UsersInRoles]
           ([UserId]
           ,[RoleId])
     VALUES
           ('474468cd-6bfd-4717-8302-60bbe3530fdb'
           ,'38b461d7-e848-46ef-8c06-ece5b618d9d1')

INSERT INTO [dbo].[Users]
           ([UserId]
           ,[ApplicationId]
           ,[UserName]
           ,[IsAnonymous]
           ,[LastActivityDate])
     VALUES
           ('c4d78e63-aa6e-4c38-9f03-a0a6311b4aa5'
		   ,'97a8aef2-6330-4e56-a81c-d359bca614b0'
           ,'systematest2'
           ,0
		   ,'1753-01-01 00:00:00.000')


INSERT INTO [dbo].[UsersInRoles]
           ([UserId]
           ,[RoleId])
     VALUES
           ('c4d78e63-aa6e-4c38-9f03-a0a6311b4aa5'
           ,'38b461d7-e848-46ef-8c06-ece5b618d9d1')

SET IDENTITY_INSERT [dbo].[Departments] ON
      INSERT INTO [dbo].[Departments]
             ([DepartmentId]
             ,[Name]
             ,[Code]
             ,[ManagingUserId]
             ,[TimeZone]
             ,[ShowWelcome])
       VALUES
             (@TestDepartmentId
             ,'Resgrid System Department'
             ,'XXXX'
             ,'474468cd-6bfd-4717-8302-60bbe3530fdb'
             ,'Pacific Standard Time'
             ,0)
SET IDENTITY_INSERT [dbo].[Departments] OFF

INSERT INTO [dbo].[DepartmentMembers]
             ([DepartmentId]
             ,[UserId]
             ,[IsAdmin]
             ,[IsDisabled]
             ,[IsHidden])
       VALUES
             (@TestDepartmentId
             ,'474468cd-6bfd-4717-8302-60bbe3530fdb'
             ,1
             ,0
             ,0)

INSERT INTO [dbo].[DepartmentMembers]
             ([DepartmentId]
             ,[UserId]
             ,[IsAdmin]
             ,[IsDisabled]
             ,[IsHidden])
       VALUES
             (@TestDepartmentId
             ,'c4d78e63-aa6e-4c38-9f03-a0a6311b4aa5'
             ,0
             ,0
             ,0)

INSERT INTO [dbo].[UserProfiles]
           ([UserId]
           ,[FirstName]
           ,[LastName]
           ,[SendEmail]
           ,[SendPush]
           ,[SendSms]
           ,[SendMessageEmail]
           ,[SendMessagePush]
           ,[SendMessageSms]
           ,[DoNotRecieveNewsletters]
           ,[MobileCarrier])
     VALUES
           ('474468cd-6bfd-4717-8302-60bbe3530fdb'
           ,'Test'
           ,'User'
           ,1
           ,0
           ,0
           ,1
           ,0
           ,0
           ,0
           ,0)

INSERT INTO [dbo].[UserProfiles]
           ([UserId]
           ,[FirstName]
           ,[LastName]
           ,[SendEmail]
           ,[SendPush]
           ,[SendSms]
           ,[SendMessageEmail]
           ,[SendMessagePush]
           ,[SendMessageSms]
           ,[DoNotRecieveNewsletters]
           ,[MobileCarrier])
     VALUES
           ('c4d78e63-aa6e-4c38-9f03-a0a6311b4aa5'
           ,'Test'
           ,'User2'
           ,1
           ,0
           ,0
           ,1
           ,0
           ,0
           ,0
           ,0)";

		public const string TestDepartmentDataSql = @"/* INTENTIONALLY LEFT BLANK */";

		public const string TestDepartmentCustomStatesSql = @"/* INTENTIONALLY LEFT BLANK */";

		public const string PlansSql = @"/* INTENTIONALLY LEFT BLANK */";

		public const string PlanFixSql = @"/* INTENTIONALLY LEFT BLANK */";

		public const string CreatingTheDefaultDepartmentSql = @"/* THIS CREATES THE DEFAULT DEPARTMENT FOR A NEW INSTALL */
INSERT INTO [dbo].[AspNetUsers]
           ([Id]
           ,[UserName]
           ,[NormalizedUserName]
           ,[Email]
           ,[NormalizedEmail]
           ,[EmailConfirmed]
           ,[PasswordHash]
           ,[SecurityStamp]
           ,[ConcurrencyStamp]
           ,[PhoneNumber]
           ,[PhoneNumberConfirmed]
           ,[TwoFactorEnabled]
           ,[LockoutEnd]
           ,[LockoutEnabled]
           ,[AccessFailedCount])
     VALUES
           ('88b16e75-a5ca-4489-8b38-eba1e4cdcba0'
           ,'admin'
           ,'ADMIN'
           ,'admin@yourcompany.local'
           ,'ADMIN@YOURCOMPANY.LOCAL'
           ,1
           ,'AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg=='
           ,'6e526153-a336-478c-9ade-d9ebcbc9748e'
           ,NULL
           ,NULL
           ,0
           ,0
           ,NULL
           ,1
           ,0)


INSERT INTO [dbo].[AspNetUserRoles]
           ([UserId]
           ,[RoleId]
           ,[IdentityRole_Id]
           ,[IdentityUser_Id])
     VALUES
           ('88b16e75-a5ca-4489-8b38-eba1e4cdcba0'
           ,'38b461d7-e848-46ef-8c06-ece5b618d9d1'
           ,NULL
           ,NULL)

		   
SET IDENTITY_INSERT [dbo].[Departments] ON
      INSERT INTO [dbo].[Departments]
             ([DepartmentId]
             ,[Name]
             ,[Code]
             ,[ManagingUserId]
             ,[TimeZone]
             ,[ShowWelcome])
       VALUES
             (2
             ,'Your Department'
             ,'ABCD'
             ,'88b16e75-a5ca-4489-8b38-eba1e4cdcba0'
             ,'Pacific Standard Time'
             ,0)
SET IDENTITY_INSERT [dbo].[Departments] OFF

INSERT INTO [dbo].[DepartmentMembers]
             ([DepartmentId]
             ,[UserId]
             ,[IsAdmin]
             ,[IsDisabled]
             ,[IsHidden])
       VALUES
             (2
             ,'88b16e75-a5ca-4489-8b38-eba1e4cdcba0'
             ,0
             ,0
             ,0)

INSERT INTO [dbo].[UserProfiles]
           ([UserId]
           ,[FirstName]
           ,[LastName]
           ,[SendEmail]
           ,[SendPush]
           ,[SendSms]
           ,[SendMessageEmail]
           ,[SendMessagePush]
           ,[SendMessageSms]
           ,[DoNotRecieveNewsletters]
           ,[MobileCarrier])
     VALUES
           ('88b16e75-a5ca-4489-8b38-eba1e4cdcba0'
           ,'Department'
           ,'Admin'
           ,0
           ,0
           ,0
           ,0
           ,0
           ,0
           ,0
           ,0)";

		#endregion SQL Statements
	}
}
