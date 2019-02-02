namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIncidentObjectAddedLocationToInventory : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Incidents",
                c => new
                    {
                        IncidentId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.IncidentId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .Index(t => t.CallId);
            
            AddColumn("dbo.Inventories", "Location", c => c.String());
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Incidents", "CallId", "dbo.Calls");
            DropIndex("dbo.Incidents", new[] { "CallId" });
            DropColumn("dbo.Inventories", "Location");
            DropTable("dbo.Incidents");
        }
    }
}
