namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddCallDispatchUnitTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallDispatchUnits",
                c => new
                    {
                        CallDispatchUnitId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        UnitId = c.Int(nullable: false),
                        DispatchCount = c.Int(nullable: false),
                        LastDispatchedOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.CallDispatchUnitId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.CallId)
                .Index(t => t.UnitId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallDispatchUnits", "UnitId", "dbo.Units");
            DropForeignKey("dbo.CallDispatchUnits", "CallId", "dbo.Calls");
            DropIndex("dbo.CallDispatchUnits", new[] { "UnitId" });
            DropIndex("dbo.CallDispatchUnits", new[] { "CallId" });
            DropTable("dbo.CallDispatchUnits");
        }
    }
}
