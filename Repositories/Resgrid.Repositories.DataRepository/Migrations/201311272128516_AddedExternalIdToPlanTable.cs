namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedExternalIdToPlanTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Plans", "ExternalId", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Plans", "ExternalId");
        }
    }
}
