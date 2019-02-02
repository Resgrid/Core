namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMarkerAndSizeToPoiTypeTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.POITypes", "Marker", c => c.String());
            AddColumn("dbo.POITypes", "Size", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.POITypes", "Size");
            DropColumn("dbo.POITypes", "Marker");
        }
    }
}
