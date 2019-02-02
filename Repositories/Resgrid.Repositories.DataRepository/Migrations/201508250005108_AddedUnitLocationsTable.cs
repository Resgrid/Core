namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitLocationsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UnitLocations",
                c => new
                    {
                        UnitLocationId = c.Int(nullable: false, identity: true),
                        UnitId = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                        Latitude = c.Decimal(precision: 18, scale: 2),
                        Longitude = c.Decimal(precision: 18, scale: 2),
                        Accuracy = c.Decimal(precision: 18, scale: 2),
                        Altitude = c.Decimal(precision: 18, scale: 2),
                        AltitudeAccuracy = c.Decimal(precision: 18, scale: 2),
                        Speed = c.Decimal(precision: 18, scale: 2),
                        Heading = c.Decimal(precision: 18, scale: 2),
                    })
                .PrimaryKey(t => t.UnitLocationId)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.UnitId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.UnitLocations", "UnitId", "dbo.Units");
            DropIndex("dbo.UnitLocations", new[] { "UnitId" });
            DropTable("dbo.UnitLocations");
        }
    }
}
