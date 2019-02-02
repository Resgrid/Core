namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedRejectedAndRejectedTimestampToAffiliate : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Affiliates", "Rejected", c => c.Boolean(nullable: false));
            AddColumn("dbo.Affiliates", "RejectedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Affiliates", "RejectedOn");
            DropColumn("dbo.Affiliates", "Rejected");
        }
    }
}
