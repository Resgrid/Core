namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ModifiedScheduleTaskTableSpecificDate : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.ScheduledTasks", "SpecifcDate", c => c.DateTime());
            AlterColumn("dbo.ScheduledTasks", "Time", c => c.String(maxLength: 50));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.ScheduledTasks", "Time", c => c.String());
            AlterColumn("dbo.ScheduledTasks", "SpecifcDate", c => c.DateTime(nullable: false));
        }
    }
}
