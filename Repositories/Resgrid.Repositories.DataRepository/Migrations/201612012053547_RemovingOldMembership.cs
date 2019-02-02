namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class RemovingOldMembership : DbMigration
	{
		public override void Up()
		{
			AddColumn("dbo.AspNetUsersExt", "CreateDate", c => c.DateTime(nullable: false));
			AddColumn("dbo.AspNetUsersExt", "LastActivityDate", c => c.DateTime());

			Sql(@"UPDATE AspNetUsersExt
								SET
										AspNetUsersExt.CreateDate = m.CreateDate
								FROM
										AspNetUsersExt ext
								INNER JOIN
										Memberships m
								ON 
										ext.UserId = m.UserId;

								UPDATE
										AspNetUsersExt
								SET
										AspNetUsersExt.LastActivityDate = u.LastActivityDate
								FROM
										AspNetUsersExt ext
								INNER JOIN
										Users u
								ON 
										ext.UserId = u.UserId;
			");

			DropForeignKey("dbo.Memberships", "ApplicationId", "dbo.Applications");
			DropForeignKey("dbo.Users", "ApplicationId", "dbo.Applications");
			DropForeignKey("dbo.Profiles", "UserId", "dbo.Users");
			DropForeignKey("dbo.Roles", "ApplicationId", "dbo.Applications");
			DropForeignKey("dbo.Roles", "User_UserId", "dbo.Users");
			DropForeignKey("dbo.Memberships", "UserId", "dbo.Users");
			DropIndex("dbo.Memberships", new[] { "UserId" });
			DropIndex("dbo.Memberships", new[] { "ApplicationId" });
			DropIndex("dbo.Users", new[] { "ApplicationId" });
			DropIndex("dbo.Profiles", new[] { "UserId" });
			DropIndex("dbo.Roles", new[] { "ApplicationId" });
			DropIndex("dbo.Roles", new[] { "User_UserId" });
			DropTable("dbo.Memberships");
			DropTable("dbo.Profiles");
			DropTable("dbo.Roles");
			DropTable("dbo.UsersInRoles");
			DropTable("dbo.Users");
			DropTable("dbo.Applications");
		}

		public override void Down()
		{
			CreateTable(
					"dbo.UsersInRoles",
					c => new
					{
						UserId = c.Guid(nullable: false),
						RoleId = c.Guid(nullable: false),
					})
					.PrimaryKey(t => new { t.UserId, t.RoleId });

			CreateTable(
					"dbo.Roles",
					c => new
					{
						RoleId = c.Guid(nullable: false),
						ApplicationId = c.Guid(nullable: false),
						RoleName = c.String(nullable: false, maxLength: 256),
						Description = c.String(maxLength: 256),
						User_UserId = c.Guid(),
					})
					.PrimaryKey(t => t.RoleId);

			CreateTable(
					"dbo.Profiles",
					c => new
					{
						UserId = c.Guid(nullable: false),
						PropertyNames = c.String(nullable: false, maxLength: 4000),
						PropertyValueStrings = c.String(nullable: false, maxLength: 4000),
						PropertyValueBinary = c.Binary(nullable: false),
						LastUpdatedDate = c.DateTime(nullable: false),
					})
					.PrimaryKey(t => t.UserId);

			CreateTable(
					"dbo.Users",
					c => new
					{
						UserId = c.Guid(nullable: false),
						ApplicationId = c.Guid(nullable: false),
						UserName = c.String(nullable: false, maxLength: 50),
						IsAnonymous = c.Boolean(nullable: false),
						LastActivityDate = c.DateTime(nullable: false),
					})
					.PrimaryKey(t => t.UserId);

			CreateTable(
					"dbo.Memberships",
					c => new
					{
						UserId = c.Guid(nullable: false),
						ApplicationId = c.Guid(nullable: false),
						Password = c.String(nullable: false, maxLength: 128),
						PasswordFormat = c.Int(nullable: false),
						PasswordSalt = c.String(nullable: false, maxLength: 128),
						Email = c.String(maxLength: 256),
						PasswordQuestion = c.String(maxLength: 256),
						PasswordAnswer = c.String(maxLength: 128),
						IsApproved = c.Boolean(nullable: false),
						IsLockedOut = c.Boolean(nullable: false),
						CreateDate = c.DateTime(nullable: false),
						LastLoginDate = c.DateTime(nullable: false),
						LastPasswordChangedDate = c.DateTime(nullable: false),
						LastLockoutDate = c.DateTime(nullable: false),
						FailedPasswordAttemptCount = c.Int(nullable: false),
						FailedPasswordAttemptWindowStart = c.DateTime(nullable: false),
						FailedPasswordAnswerAttemptCount = c.Int(nullable: false),
						FailedPasswordAnswerAttemptWindowsStart = c.DateTime(nullable: false),
						Comment = c.String(maxLength: 256),
					})
					.PrimaryKey(t => t.UserId);

			CreateTable(
					"dbo.Applications",
					c => new
					{
						ApplicationId = c.Guid(nullable: false),
						ApplicationName = c.String(nullable: false, maxLength: 235),
						Description = c.String(maxLength: 256),
					})
					.PrimaryKey(t => t.ApplicationId);

			DropColumn("dbo.AspNetUsersExt", "LastActivityDate");
			DropColumn("dbo.AspNetUsersExt", "CreateDate");
			CreateIndex("dbo.Roles", "User_UserId");
			CreateIndex("dbo.Roles", "ApplicationId");
			CreateIndex("dbo.Profiles", "UserId");
			CreateIndex("dbo.Users", "ApplicationId");
			CreateIndex("dbo.Memberships", "ApplicationId");
			CreateIndex("dbo.Memberships", "UserId");
			AddForeignKey("dbo.Memberships", "UserId", "dbo.Users", "UserId");
			AddForeignKey("dbo.Roles", "User_UserId", "dbo.Users", "UserId");
			AddForeignKey("dbo.Roles", "ApplicationId", "dbo.Applications", "ApplicationId", cascadeDelete: true);
			AddForeignKey("dbo.Profiles", "UserId", "dbo.Users", "UserId");
			AddForeignKey("dbo.Users", "ApplicationId", "dbo.Applications", "ApplicationId", cascadeDelete: true);
			AddForeignKey("dbo.Memberships", "ApplicationId", "dbo.Applications", "ApplicationId", cascadeDelete: true);
		}
	}
}
