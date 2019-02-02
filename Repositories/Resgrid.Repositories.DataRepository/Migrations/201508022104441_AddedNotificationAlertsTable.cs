namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNotificationAlertsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.NotificationAlerts",
                c => new
                    {
                        NotificationAlertId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        DepartmentGroupId = c.Int(),
                        EventType = c.Int(nullable: false),
                        Opened = c.DateTime(nullable: false),
                        Closed = c.DateTime(),
                        ManuallyClosed = c.Boolean(nullable: false),
                        Data = c.String(),
                        ManualNote = c.String(),
                    })
                .PrimaryKey(t => t.NotificationAlertId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.DepartmentGroups", t => t.DepartmentGroupId)
                .Index(t => t.DepartmentId)
                .Index(t => t.DepartmentGroupId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.NotificationAlerts", "DepartmentGroupId", "dbo.DepartmentGroups");
            DropForeignKey("dbo.NotificationAlerts", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.NotificationAlerts", new[] { "DepartmentGroupId" });
            DropIndex("dbo.NotificationAlerts", new[] { "DepartmentId" });
            DropTable("dbo.NotificationAlerts");
        }
    }
}
