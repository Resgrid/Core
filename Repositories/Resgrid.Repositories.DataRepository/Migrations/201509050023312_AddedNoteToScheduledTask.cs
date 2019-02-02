namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNoteToScheduledTask : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ScheduledTasks", "Note", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ScheduledTasks", "Note");
        }
    }
}
