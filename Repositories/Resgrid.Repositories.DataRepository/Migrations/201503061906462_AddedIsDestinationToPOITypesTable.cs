namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIsDestinationToPOITypesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.POITypes", "IsDestination", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.POITypes", "IsDestination");
        }
    }
}
