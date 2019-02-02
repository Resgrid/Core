namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUpgradedPaymentIdToPaymentsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "Payment_PaymentId", c => c.Int());
            AddForeignKey("dbo.Payments", "Payment_PaymentId", "dbo.Payments", "PaymentId");
            CreateIndex("dbo.Payments", "Payment_PaymentId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.Payments", new[] { "Payment_PaymentId" });
            DropForeignKey("dbo.Payments", "Payment_PaymentId", "dbo.Payments");
            DropColumn("dbo.Payments", "Payment_PaymentId");
        }
    }
}
