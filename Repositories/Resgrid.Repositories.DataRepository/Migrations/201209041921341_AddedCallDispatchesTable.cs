namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallDispatchesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallDispatches",
                c => new
                    {
                        CallDispatchId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        GroupId = c.Int(),
                        ActionLogId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CallDispatchId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .ForeignKey("dbo.ActionLogs", t => t.ActionLogId)
                .Index(t => t.CallId)
                .Index(t => t.UserId)
                .Index(t => t.ActionLogId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.CallDispatches", new[] { "ActionLogId" });
            DropIndex("dbo.CallDispatches", new[] { "UserId" });
            DropIndex("dbo.CallDispatches", new[] { "CallId" });
            DropForeignKey("dbo.CallDispatches", "ActionLogId", "dbo.ActionLogs");
            DropForeignKey("dbo.CallDispatches", "UserId", "dbo.Users");
            DropForeignKey("dbo.CallDispatches", "CallId", "dbo.Calls");
            DropTable("dbo.CallDispatches");
        }
    }
}
