namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitAndTypeToIncidentLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.IncidentLogs", "UnitId", c => c.Int(nullable: false));
            AddColumn("dbo.IncidentLogs", "Type", c => c.Int(nullable: false));
            CreateIndex("dbo.IncidentLogs", "UnitId");
            AddForeignKey("dbo.IncidentLogs", "UnitId", "dbo.Units", "UnitId", cascadeDelete: true);
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.IncidentLogs", "UnitId", "dbo.Units");
            DropIndex("dbo.IncidentLogs", new[] { "UnitId" });
            DropColumn("dbo.IncidentLogs", "Type");
            DropColumn("dbo.IncidentLogs", "UnitId");
        }
    }
}
