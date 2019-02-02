namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGeoLocationDataToActionLogs : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ActionLogs", "GeoLocationData", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ActionLogs", "GeoLocationData");
        }
    }
}
