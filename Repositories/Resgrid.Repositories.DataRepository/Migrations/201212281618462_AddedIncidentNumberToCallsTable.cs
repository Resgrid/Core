namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIncidentNumberToCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "IncidentNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Calls", "IncidentNumber");
        }
    }
}
