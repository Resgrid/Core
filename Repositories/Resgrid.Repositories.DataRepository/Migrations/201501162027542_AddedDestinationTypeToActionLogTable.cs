namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDestinationTypeToActionLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ActionLogs", "DestinationType", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ActionLogs", "DestinationType");
        }
    }
}
