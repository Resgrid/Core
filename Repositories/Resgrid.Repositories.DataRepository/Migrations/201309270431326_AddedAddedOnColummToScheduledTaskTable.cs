namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedAddedOnColummToScheduledTaskTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ScheduledTasks", "AddedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ScheduledTasks", "AddedOn");
        }
    }
}
