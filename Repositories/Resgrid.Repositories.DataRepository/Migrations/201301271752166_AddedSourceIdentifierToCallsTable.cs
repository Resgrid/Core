namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSourceIdentifierToCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "SourceIdentifier", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "SourceIdentifier");
        }
    }
}
