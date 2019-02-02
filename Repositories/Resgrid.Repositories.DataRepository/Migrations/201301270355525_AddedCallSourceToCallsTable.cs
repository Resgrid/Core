namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallSourceToCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "CallSource", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "CallSource");
        }
    }
}
