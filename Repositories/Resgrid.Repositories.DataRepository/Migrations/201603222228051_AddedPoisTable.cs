namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPoisTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Pois",
                c => new
                    {
                        PoiId = c.Int(nullable: false, identity: true),
                        PoiTypeId = c.Int(nullable: false),
                        Longitude = c.Double(nullable: false),
                        Latitude = c.Double(nullable: false),
                        Note = c.String(),
                    })
                .PrimaryKey(t => t.PoiId)
                .ForeignKey("dbo.POITypes", t => t.PoiTypeId, cascadeDelete: true)
                .Index(t => t.PoiTypeId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Pois", "PoiTypeId", "dbo.POITypes");
            DropIndex("dbo.Pois", new[] { "PoiTypeId" });
            DropTable("dbo.Pois");
        }
    }
}
