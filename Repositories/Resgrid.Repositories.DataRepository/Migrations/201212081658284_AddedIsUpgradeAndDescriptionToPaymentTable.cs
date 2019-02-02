namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIsUpgradeAndDescriptionToPaymentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "IsUpgrade", c => c.Boolean(nullable: false));
            AddColumn("dbo.Payments", "Description", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Payments", "Description");
            DropColumn("dbo.Payments", "IsUpgrade");
        }
    }
}
