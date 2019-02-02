namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNotifitedToTrainingTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Trainings", "Notified", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Trainings", "Notified");
        }
    }
}
