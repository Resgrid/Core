namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class Addedw3wToCallTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "W3W", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "W3W");
        }
    }
}
