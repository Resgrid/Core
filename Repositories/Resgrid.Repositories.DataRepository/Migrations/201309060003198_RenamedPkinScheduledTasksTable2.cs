namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RenamedPkinScheduledTasksTable2 : DbMigration
    {
        public override void Up()
        {
			AddColumn("dbo.ScheduledTasks", "ScheduledTaskId", c => c.Int(nullable: false, identity: true));
            AddPrimaryKey("dbo.ScheduledTasks", "ScheduledTaskId");
        }
        
        public override void Down()
        {
            AddColumn("dbo.ScheduledTasks", "UserScheduleId", c => c.Int(nullable: false, identity: true));
            DropPrimaryKey("dbo.ScheduledTasks", new[] { "ScheduledTaskId" });
            AddPrimaryKey("dbo.ScheduledTasks", "UserScheduleId");
            DropColumn("dbo.ScheduledTasks", "ScheduledTaskId");
        }
    }
}
