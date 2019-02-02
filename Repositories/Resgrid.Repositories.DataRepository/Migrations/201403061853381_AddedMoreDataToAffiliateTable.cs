namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMoreDataToAffiliateTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Affiliates", "TimeZone", c => c.String());
            AddColumn("dbo.Affiliates", "CreatedOn", c => c.DateTime(nullable: false));
            AddColumn("dbo.Affiliates", "ApprovedOn", c => c.DateTime());
            AddColumn("dbo.Affiliates", "DeactivatedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Affiliates", "DeactivatedOn");
            DropColumn("dbo.Affiliates", "ApprovedOn");
            DropColumn("dbo.Affiliates", "CreatedOn");
            DropColumn("dbo.Affiliates", "TimeZone");
        }
    }
}
