namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeAndProcessedToPaymentProviderTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PaymentProviderEvents", "Type", c => c.String());
            AddColumn("dbo.PaymentProviderEvents", "Processed", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.PaymentProviderEvents", "Processed");
            DropColumn("dbo.PaymentProviderEvents", "Type");
        }
    }
}
