namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ModifiedScheduleTaskTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ScheduledTasks", "Time", c => c.String());
            DropColumn("dbo.ScheduledTasks", "Hours");
            DropColumn("dbo.ScheduledTasks", "Minutes");
        }
        
        public override void Down()
        {
            AddColumn("dbo.ScheduledTasks", "Minutes", c => c.Int(nullable: false));
            AddColumn("dbo.ScheduledTasks", "Hours", c => c.Int(nullable: false));
            DropColumn("dbo.ScheduledTasks", "Time");
        }
    }
}
