namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedContactNameNumberToCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "ContactName", c => c.String());
            AddColumn("dbo.Calls", "ContactNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "ContactNumber");
            DropColumn("dbo.Calls", "ContactName");
        }
    }
}
