namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedScheduledTaskLogTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ScheduledTaskLogs",
                c => new
                    {
                        ScheduledTaskLogId = c.Int(nullable: false, identity: true),
                        ScheduledTaskId = c.Int(nullable: false),
                        RunDate = c.DateTime(nullable: false),
                        Successful = c.Boolean(nullable: false),
                        Data = c.String(maxLength: 3000),
                    })
                .PrimaryKey(t => t.ScheduledTaskLogId)
                .ForeignKey("dbo.ScheduledTasks", t => t.ScheduledTaskId, cascadeDelete: true)
                .Index(t => t.ScheduledTaskId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.ScheduledTaskLogs", new[] { "ScheduledTaskId" });
            DropForeignKey("dbo.ScheduledTaskLogs", "ScheduledTaskId", "dbo.ScheduledTasks");
            DropTable("dbo.ScheduledTaskLogs");
        }
    }
}
