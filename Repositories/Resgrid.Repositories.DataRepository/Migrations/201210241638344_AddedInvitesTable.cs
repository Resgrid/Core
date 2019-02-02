namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedInvitesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Invites",
                c => new
                    {
                        InviteId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Code = c.Guid(nullable: false),
                        EmailAddress = c.String(nullable: false),
                        SendingUserId = c.Guid(nullable: false),
                        SentOn = c.DateTime(nullable: false),
                        CompletedOn = c.DateTime(),
                        CompletedUserId = c.Guid(),
                    })
                .PrimaryKey(t => t.InviteId)
                .ForeignKey("dbo.Users", t => t.SendingUserId, cascadeDelete: true)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .Index(t => t.SendingUserId)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.Invites", new[] { "DepartmentId" });
            DropIndex("dbo.Invites", new[] { "SendingUserId" });
            DropForeignKey("dbo.Invites", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.Invites", "SendingUserId", "dbo.Users");
            DropTable("dbo.Invites");
        }
    }
}
