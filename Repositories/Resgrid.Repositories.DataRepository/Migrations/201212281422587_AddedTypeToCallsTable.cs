namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedTypeToCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "Type", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "Type");
        }
    }
}
