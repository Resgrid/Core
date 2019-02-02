namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RenamedPkinScheduledTasksTable : DbMigration
    {
        public override void Up()
        {
            DropPrimaryKey("dbo.ScheduledTasks", new[] { "UserScheduleId" });
			DropColumn("dbo.ScheduledTasks", "UserScheduleId");
        }
        
        public override void Down()
        {

        }
    }
}
