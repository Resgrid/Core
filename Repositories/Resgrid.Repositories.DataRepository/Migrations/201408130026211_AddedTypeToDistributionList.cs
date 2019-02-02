namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeToDistributionList : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DistributionLists", "Type", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DistributionLists", "Type");
        }
    }
}
