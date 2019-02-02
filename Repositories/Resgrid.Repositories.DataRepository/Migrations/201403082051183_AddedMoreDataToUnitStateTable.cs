namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMoreDataToUnitStateTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UnitStates", "LocalTimestamp", c => c.DateTime());
            AddColumn("dbo.UnitStates", "Note", c => c.String());
            AddColumn("dbo.UnitStates", "Latitude", c => c.Decimal(precision: 10, scale: 7));
            AddColumn("dbo.UnitStates", "Longitude", c => c.Decimal(precision: 10, scale: 7));
            AddColumn("dbo.UnitStates", "Accuracy", c => c.Decimal(precision: 6, scale: 2));
            AddColumn("dbo.UnitStates", "Altitude", c => c.Decimal(precision: 7, scale: 2));
            AddColumn("dbo.UnitStates", "AltitudeAccuracy", c => c.Decimal(precision: 6, scale: 2));
            AddColumn("dbo.UnitStates", "Speed", c => c.Decimal(precision: 5, scale: 2));
            AddColumn("dbo.UnitStates", "Heading", c => c.Decimal(precision: 5, scale: 2));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UnitStates", "Heading");
            DropColumn("dbo.UnitStates", "Speed");
            DropColumn("dbo.UnitStates", "AltitudeAccuracy");
            DropColumn("dbo.UnitStates", "Altitude");
            DropColumn("dbo.UnitStates", "Accuracy");
            DropColumn("dbo.UnitStates", "Longitude");
            DropColumn("dbo.UnitStates", "Latitude");
            DropColumn("dbo.UnitStates", "Note");
            DropColumn("dbo.UnitStates", "LocalTimestamp");
        }
    }
}
