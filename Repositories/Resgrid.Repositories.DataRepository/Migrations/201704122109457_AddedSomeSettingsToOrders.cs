namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSomeSettingsToOrders : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ResourceOrders", "Visibility", c => c.Int(nullable: false));
            AddColumn("dbo.ResourceOrders", "Range", c => c.Int(nullable: false));
            AddColumn("dbo.ResourceOrders", "OriginLatitude", c => c.Double());
            AddColumn("dbo.ResourceOrders", "OriginLongitude", c => c.Double());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ResourceOrders", "OriginLongitude");
            DropColumn("dbo.ResourceOrders", "OriginLatitude");
            DropColumn("dbo.ResourceOrders", "Range");
            DropColumn("dbo.ResourceOrders", "Visibility");
        }
    }
}
