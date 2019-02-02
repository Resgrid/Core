namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLocationToCallAttachments : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CallAttachments", "Latitude", c => c.Decimal(precision: 18, scale: 2));
            AddColumn("dbo.CallAttachments", "Longitude", c => c.Decimal(precision: 18, scale: 2));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CallAttachments", "Longitude");
            DropColumn("dbo.CallAttachments", "Latitude");
        }
    }
}
