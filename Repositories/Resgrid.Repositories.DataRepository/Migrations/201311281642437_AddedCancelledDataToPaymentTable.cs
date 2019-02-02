namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCancelledDataToPaymentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "Cancelled", c => c.Boolean(nullable: false));
            AddColumn("dbo.Payments", "CancelledOn", c => c.DateTime());
            AddColumn("dbo.Payments", "CancelledData", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Payments", "CancelledData");
            DropColumn("dbo.Payments", "CancelledOn");
            DropColumn("dbo.Payments", "Cancelled");
        }
    }
}
