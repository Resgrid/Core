namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatesToTheCallsTable2 : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Calls", "Type", c => c.String(maxLength: 100));
            AlterColumn("dbo.Calls", "IncidentNumber", c => c.String(maxLength: 250));
            AlterColumn("dbo.Calls", "SourceIdentifier", c => c.String(maxLength: 500));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Calls", "SourceIdentifier", c => c.String());
            AlterColumn("dbo.Calls", "IncidentNumber", c => c.String());
            AlterColumn("dbo.Calls", "Type", c => c.String());
        }
    }
}
