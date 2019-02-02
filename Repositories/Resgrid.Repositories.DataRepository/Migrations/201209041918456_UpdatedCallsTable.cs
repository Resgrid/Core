namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedCallsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Users", "Call_CallId", c => c.Int());
            AddColumn("dbo.Calls", "Priority", c => c.Int(nullable: false));
            AddColumn("dbo.Calls", "IsCritical", c => c.Boolean(nullable: false));
            AddColumn("dbo.Calls", "MapPage", c => c.String(maxLength: 100));
            AddColumn("dbo.Calls", "CompletedNotes", c => c.String(maxLength: 4000));
            AddColumn("dbo.Calls", "Address", c => c.String(maxLength: 500));
            AddColumn("dbo.Calls", "GeoLocationData", c => c.String(maxLength: 4000));
            AddColumn("dbo.Calls", "ClosedByUserId", c => c.Guid());
            AddColumn("dbo.Calls", "ClosedOn", c => c.DateTime());
            AddColumn("dbo.Calls", "State", c => c.Int(nullable: false));
            AddColumn("dbo.Calls", "IsDeleted", c => c.Boolean(nullable: false));
            AddForeignKey("dbo.Users", "Call_CallId", "dbo.Calls", "CallId");
            CreateIndex("dbo.Users", "Call_CallId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.Users", new[] { "Call_CallId" });
            DropForeignKey("dbo.Users", "Call_CallId", "dbo.Calls");
            DropColumn("dbo.Calls", "IsDeleted");
            DropColumn("dbo.Calls", "State");
            DropColumn("dbo.Calls", "ClosedOn");
            DropColumn("dbo.Calls", "ClosedByUserId");
            DropColumn("dbo.Calls", "GeoLocationData");
            DropColumn("dbo.Calls", "Address");
            DropColumn("dbo.Calls", "CompletedNotes");
            DropColumn("dbo.Calls", "MapPage");
            DropColumn("dbo.Calls", "IsCritical");
            DropColumn("dbo.Calls", "Priority");
            DropColumn("dbo.Users", "Call_CallId");
        }
    }
}
