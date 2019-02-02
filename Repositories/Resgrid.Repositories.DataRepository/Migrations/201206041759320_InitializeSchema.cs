namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class InitializeSchema : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "Applications",
                c => new
                    {
                        ApplicationId = c.Guid(nullable: false),
                        ApplicationName = c.String(nullable: false, maxLength: 235),
                        Description = c.String(maxLength: 256),
                    })
                .PrimaryKey(t => t.ApplicationId);
            
            CreateTable(
                "Memberships",
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
                .PrimaryKey(t => t.UserId)
                .ForeignKey("Users", t => t.UserId)
                .ForeignKey("Applications", t => t.ApplicationId, cascadeDelete: true)
                .Index(t => t.UserId)
                .Index(t => t.ApplicationId);
            
            CreateTable(
                "Users",
                c => new
                    {
                        UserId = c.Guid(nullable: false),
                        ApplicationId = c.Guid(nullable: false),
                        UserName = c.String(nullable: false, maxLength: 50),
                        IsAnonymous = c.Boolean(nullable: false),
                        LastActivityDate = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.UserId)
                .ForeignKey("Applications", t => t.ApplicationId, cascadeDelete: true)
                .Index(t => t.ApplicationId);
            
            CreateTable(
                "Profiles",
                c => new
                    {
                        UserId = c.Guid(nullable: false),
                        PropertyNames = c.String(nullable: false, maxLength: 4000),
                        PropertyValueStrings = c.String(nullable: false, maxLength: 4000),
                        PropertyValueBinary = c.Binary(nullable: false),
                        LastUpdatedDate = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.UserId)
                .ForeignKey("Users", t => t.UserId)
                .Index(t => t.UserId);
            
            CreateTable(
                "Roles",
                c => new
                    {
                        RoleId = c.Guid(nullable: false),
                        ApplicationId = c.Guid(nullable: false),
                        RoleName = c.String(nullable: false, maxLength: 256),
                        Description = c.String(maxLength: 256),
                        User_UserId = c.Guid(),
                    })
                .PrimaryKey(t => t.RoleId)
                .ForeignKey("Applications", t => t.ApplicationId, cascadeDelete: true)
                .ForeignKey("Users", t => t.User_UserId)
                .Index(t => t.ApplicationId)
                .Index(t => t.User_UserId);
            
            CreateTable(
                "UsersInRoles",
                c => new
                    {
                        UserId = c.Guid(nullable: false),
                        RoleId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => new { t.UserId, t.RoleId });
            
            CreateTable(
                "Departments",
                c => new
                    {
                        DepartmentId = c.Int(nullable: false, identity: true),
                        Name = c.String(nullable: false, maxLength: 150),
                        Code = c.String(maxLength: 4),
                        ManagingUserId = c.Guid(nullable: false),
                        ShowWelcome = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentId)
                .ForeignKey("Users", t => t.ManagingUserId, cascadeDelete: true)
                .Index(t => t.ManagingUserId);
            
            CreateTable(
                "DepartmentMembers",
                c => new
                    {
                        DepartmentMemberId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentMemberId)
                .ForeignKey("Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("Users", t => t.UserId, cascadeDelete: false)
                .Index(t => t.DepartmentId)
                .Index(t => t.UserId);
            
            CreateTable(
                "ActionLogs",
                c => new
                    {
                        ActionLogId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        DepartmentId = c.Int(nullable: false),
                        ActionTypeId = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.ActionLogId)
                .ForeignKey("Users", t => t.UserId, cascadeDelete: true)
                .ForeignKey("Departments", t => t.DepartmentId, cascadeDelete: false)
                .Index(t => t.UserId)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("ActionLogs", new[] { "DepartmentId" });
            DropIndex("ActionLogs", new[] { "UserId" });
            DropIndex("DepartmentMembers", new[] { "UserId" });
            DropIndex("DepartmentMembers", new[] { "DepartmentId" });
            DropIndex("Departments", new[] { "ManagingUserId" });
            DropIndex("Roles", new[] { "User_UserId" });
            DropIndex("Roles", new[] { "ApplicationId" });
            DropIndex("Profiles", new[] { "UserId" });
            DropIndex("Users", new[] { "ApplicationId" });
            DropIndex("Memberships", new[] { "ApplicationId" });
            DropIndex("Memberships", new[] { "UserId" });
            DropForeignKey("ActionLogs", "DepartmentId", "Departments");
            DropForeignKey("ActionLogs", "UserId", "Users");
            DropForeignKey("DepartmentMembers", "UserId", "Users");
            DropForeignKey("DepartmentMembers", "DepartmentId", "Departments");
            DropForeignKey("Departments", "ManagingUserId", "Users");
            DropForeignKey("Roles", "User_UserId", "Users");
            DropForeignKey("Roles", "ApplicationId", "Applications");
            DropForeignKey("Profiles", "UserId", "Users");
            DropForeignKey("Users", "ApplicationId", "Applications");
            DropForeignKey("Memberships", "ApplicationId", "Applications");
            DropForeignKey("Memberships", "UserId", "Users");
            DropTable("ActionLogs");
            DropTable("DepartmentMembers");
            DropTable("Departments");
            DropTable("UsersInRoles");
            DropTable("Roles");
            DropTable("Profiles");
            DropTable("Users");
            DropTable("Memberships");
            DropTable("Applications");
        }
    }
}
