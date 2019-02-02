namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDestinationToActionLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ActionLogs", "DestinationId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ActionLogs", "DestinationId");
        }
    }
}
