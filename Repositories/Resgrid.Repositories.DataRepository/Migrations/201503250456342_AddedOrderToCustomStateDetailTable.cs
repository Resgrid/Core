namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedOrderToCustomStateDetailTable : DbMigration
    {
        public override void Up()
        {
					AddColumn("dbo.CustomStateDetails", "Order", c => c.Int(nullable: false, defaultValue: 0));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CustomStateDetails", "Order");
        }
    }
}
