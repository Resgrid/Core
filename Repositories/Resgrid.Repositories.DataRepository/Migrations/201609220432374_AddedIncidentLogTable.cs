namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIncidentLogTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.IncidentLogs",
                c => new
                    {
                        IncidentLogId = c.Int(nullable: false, identity: true),
                        IncidentId = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                        Description = c.String(),
                    })
                .PrimaryKey(t => t.IncidentLogId)
                .ForeignKey("dbo.Incidents", t => t.IncidentId, cascadeDelete: true)
                .Index(t => t.IncidentId);
            
            AddColumn("dbo.Incidents", "CommandDefinitionId", c => c.Int(nullable: false));
            AddColumn("dbo.Incidents", "Name", c => c.String());
            AddColumn("dbo.Incidents", "Start", c => c.DateTime(nullable: false));
            AddColumn("dbo.Incidents", "End", c => c.DateTime());
            AddColumn("dbo.Incidents", "State", c => c.Int(nullable: false));
            CreateIndex("dbo.Incidents", "CommandDefinitionId");
            AddForeignKey("dbo.Incidents", "CommandDefinitionId", "dbo.CommandDefinitions", "CommandDefinitionId", cascadeDelete: true);
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.IncidentLogs", "IncidentId", "dbo.Incidents");
            DropForeignKey("dbo.Incidents", "CommandDefinitionId", "dbo.CommandDefinitions");
            DropIndex("dbo.Incidents", new[] { "CommandDefinitionId" });
            DropIndex("dbo.IncidentLogs", new[] { "IncidentId" });
            DropColumn("dbo.Incidents", "State");
            DropColumn("dbo.Incidents", "End");
            DropColumn("dbo.Incidents", "Start");
            DropColumn("dbo.Incidents", "Name");
            DropColumn("dbo.Incidents", "CommandDefinitionId");
            DropTable("dbo.IncidentLogs");
        }
    }
}
