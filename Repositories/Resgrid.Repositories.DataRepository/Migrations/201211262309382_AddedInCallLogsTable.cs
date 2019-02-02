namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedInCallLogsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallLogs",
                c => new
                    {
                        CallLogId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Narrative = c.String(nullable: false),
                        CallId = c.Int(nullable: false),
                        LoggedOn = c.DateTime(nullable: false),
                        LoggedByUserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.CallLogId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.Users", t => t.LoggedByUserId)
                .Index(t => t.CallId)
                .Index(t => t.DepartmentId)
                .Index(t => t.LoggedByUserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.CallLogs", new[] { "LoggedByUserId" });
            DropIndex("dbo.CallLogs", new[] { "DepartmentId" });
            DropIndex("dbo.CallLogs", new[] { "CallId" });
            DropForeignKey("dbo.CallLogs", "LoggedByUserId", "dbo.Users");
            DropForeignKey("dbo.CallLogs", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.CallLogs", "CallId", "dbo.Calls");
            DropTable("dbo.CallLogs");
        }
    }
}
