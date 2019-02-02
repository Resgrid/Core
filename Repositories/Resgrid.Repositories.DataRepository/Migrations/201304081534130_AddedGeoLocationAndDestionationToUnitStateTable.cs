namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGeoLocationAndDestionationToUnitStateTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UnitStates", "GeoLocationData", c => c.String());
            AddColumn("dbo.UnitStates", "DestinationId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UnitStates", "DestinationId");
            DropColumn("dbo.UnitStates", "GeoLocationData");
        }
    }
}
