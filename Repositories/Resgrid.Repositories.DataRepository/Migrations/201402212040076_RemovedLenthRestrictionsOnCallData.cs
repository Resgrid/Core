namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovedLenthRestrictionsOnCallData : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Calls", "Type", c => c.String());
            AlterColumn("dbo.Calls", "IncidentNumber", c => c.String());
            AlterColumn("dbo.Calls", "Name", c => c.String(nullable: false));
            AlterColumn("dbo.Calls", "NatureOfCall", c => c.String(nullable: false));
            AlterColumn("dbo.Calls", "MapPage", c => c.String());
            AlterColumn("dbo.Calls", "Notes", c => c.String());
            AlterColumn("dbo.Calls", "CompletedNotes", c => c.String());
            AlterColumn("dbo.Calls", "Address", c => c.String());
            AlterColumn("dbo.Calls", "GeoLocationData", c => c.String());
            AlterColumn("dbo.Calls", "SourceIdentifier", c => c.String());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Calls", "SourceIdentifier", c => c.String(maxLength: 500));
            AlterColumn("dbo.Calls", "GeoLocationData", c => c.String(maxLength: 4000));
            AlterColumn("dbo.Calls", "Address", c => c.String(maxLength: 500));
            AlterColumn("dbo.Calls", "CompletedNotes", c => c.String(maxLength: 4000));
            AlterColumn("dbo.Calls", "Notes", c => c.String(maxLength: 4000));
            AlterColumn("dbo.Calls", "MapPage", c => c.String(maxLength: 100));
            AlterColumn("dbo.Calls", "NatureOfCall", c => c.String(nullable: false, maxLength: 500));
            AlterColumn("dbo.Calls", "Name", c => c.String(nullable: false, maxLength: 100));
            AlterColumn("dbo.Calls", "IncidentNumber", c => c.String(maxLength: 250));
            AlterColumn("dbo.Calls", "Type", c => c.String(maxLength: 100));
        }
    }
}
