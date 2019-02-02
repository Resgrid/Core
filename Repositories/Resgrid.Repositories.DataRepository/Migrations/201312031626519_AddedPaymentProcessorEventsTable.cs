namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPaymentProcessorEventsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PaymentProviderEvents",
                c => new
                    {
                        PaymentProviderEventId = c.Int(nullable: false, identity: true),
                        ProviderType = c.Int(nullable: false),
                        CustomerId = c.String(nullable: false),
                        RecievedOn = c.DateTime(nullable: false),
                        Data = c.String(nullable: false),
                    })
                .PrimaryKey(t => t.PaymentProviderEventId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.PaymentProviderEvents");
        }
    }
}
