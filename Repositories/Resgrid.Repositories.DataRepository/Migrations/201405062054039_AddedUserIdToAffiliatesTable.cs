namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUserIdToAffiliatesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Affiliates", "UserId", c => c.Guid());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Affiliates", "UserId");
        }
    }
}
