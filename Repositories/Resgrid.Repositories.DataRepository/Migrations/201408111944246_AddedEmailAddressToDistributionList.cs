namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedEmailAddressToDistributionList : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DistributionLists", "EmailAddress", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DistributionLists", "EmailAddress");
        }
    }
}
