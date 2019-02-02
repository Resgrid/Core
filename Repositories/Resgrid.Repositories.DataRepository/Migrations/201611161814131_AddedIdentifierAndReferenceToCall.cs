namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIdentifierAndReferenceToCall : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "ExternalIdentifier", c => c.String());
            AddColumn("dbo.Calls", "ReferenceNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "ReferenceNumber");
            DropColumn("dbo.Calls", "ExternalIdentifier");
        }
    }
}
