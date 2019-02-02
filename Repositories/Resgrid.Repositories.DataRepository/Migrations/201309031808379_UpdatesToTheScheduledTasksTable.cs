namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatesToTheScheduledTasksTable : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.ScheduledTasks", "Data", c => c.String(maxLength: 3000));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.ScheduledTasks", "Data", c => c.String());
        }
    }
}
