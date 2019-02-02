namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentNotificationsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentNotifications",
                c => new
                    {
                        DepartmentNotificationId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        EventType = c.Int(nullable: false),
                        UsersToNotify = c.String(),
                        RolesToNotify = c.String(),
                        GroupsToNotify = c.String(),
                        LockToGroup = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentNotificationId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentNotifications", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.DepartmentNotifications", new[] { "DepartmentId" });
            DropTable("dbo.DepartmentNotifications");
        }
    }
}
