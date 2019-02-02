namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovingExpiryNullabilityForInvType : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.InventoryTypes", "ExpiresDays", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.InventoryTypes", "ExpiresDays", c => c.Int());
        }
    }
}
