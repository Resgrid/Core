namespace Resgrid.Repositories.DataRepository.Migrations
{
		using System;
		using System.Data.Entity.Migrations;
		
		public partial class MigratingMembershipToIdentity : DbMigration
		{
				public override void Up()
				{
					Sql(@"
								/* Migrate users */
								INSERT INTO AspNetUsers (Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,EmailConfirmed,PhoneNumber,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnd,LockoutEnabled,AccessFailedCount,Email,NormalizedEmail)
								SELECT Users.UserId, Users.UserName, Users.UserName,
								(Memberships.Password+'|'+CAST(Memberships.PasswordFormat as varchar)+'|'+Memberships.PasswordSalt),
								NewID(), 1, NULL, 0, 0, CASE WHEN Memberships.IsLockedOut = 1 THEN DATEADD(YEAR, 1000, SYSUTCDATETIME()) ELSE NULL END, 1, 0, Memberships.Email, Memberships.Email
								FROM Users
								LEFT OUTER JOIN Memberships ON Memberships.ApplicationId = Users.ApplicationId 
								AND Users.UserId = Memberships.UserId
								LEFT OUTER JOIN AspNetUsers ON Memberships.UserId = AspNetUsers.Id
								WHERE AspNetUsers.Id IS NULL

								/* Migrate user question/answer */
								INSERT INTO AspNetUsersExt (UserId, SecurityQuestion, SecurityAnswer, SecurityAnswerSalt)
								SELECT Users.UserId, Memberships.PasswordQuestion, PasswordAnswer, PasswordSalt
								FROM Users
								LEFT OUTER JOIN Memberships ON Memberships.ApplicationId = Users.ApplicationId 
								AND Users.UserId = Memberships.UserId
								LEFT OUTER JOIN AspNetUsersExt ON Memberships.UserId = AspNetUsersExt.UserId
								LEFT OUTER JOIN AspNetUsers ON Memberships.UserId = AspNetUsers.Id
								WHERE AspNetUsers.Id IS NOT NULL AND AspNetUsersExt.UserId IS NULL

								-- Migrate over roles
								INSERT INTO [AspNetRoles] ([Id],[Name],[NormalizedName],[ConcurrencyStamp])
								SELECT [RoleId],[RoleName],[RoleName],NULL FROM [Roles]

								INSERT INTO [AspNetUserRoles]([RoleId],[UserId])
								SELECT [RoleId],[UserId] FROM [UsersInRoles]
					");
				}
				
				public override void Down()
				{
				}
		}
}
