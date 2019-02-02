namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUpgradedPaymentIdToPaymentsTable1 : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "UpgradedPaymentId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Payments", "UpgradedPaymentId");
        }
    }
}
