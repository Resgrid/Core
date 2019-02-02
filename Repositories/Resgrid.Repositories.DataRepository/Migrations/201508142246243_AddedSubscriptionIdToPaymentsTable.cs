namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSubscriptionIdToPaymentsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Payments", "SubscriptionId", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Payments", "SubscriptionId");
        }
    }
}
