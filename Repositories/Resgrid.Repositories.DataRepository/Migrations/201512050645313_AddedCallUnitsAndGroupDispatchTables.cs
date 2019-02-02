namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallUnitsAndGroupDispatchTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallDispatchGroups",
                c => new
                    {
                        CallDispatchGroupId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        DepartmentGroupId = c.Int(nullable: false),
                        DispatchCount = c.Int(nullable: false),
                        LastDispatchedOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.CallDispatchGroupId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.DepartmentGroups", t => t.DepartmentGroupId)
                .Index(t => t.CallId)
                .Index(t => t.DepartmentGroupId);
            
            CreateTable(
                "dbo.CallUnits",
                c => new
                    {
                        CallUnitId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        UnitId = c.Int(nullable: false),
                        DispatchCount = c.Int(nullable: false),
                        LastDispatchedOn = c.DateTime(),
                        UnitStateId = c.Int(),
                    })
                .PrimaryKey(t => t.CallUnitId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .ForeignKey("dbo.UnitStates", t => t.UnitStateId)
                .Index(t => t.CallId)
                .Index(t => t.UnitId)
                .Index(t => t.UnitStateId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallUnits", "UnitStateId", "dbo.UnitStates");
            DropForeignKey("dbo.CallUnits", "UnitId", "dbo.Units");
            DropForeignKey("dbo.CallUnits", "CallId", "dbo.Calls");
            DropForeignKey("dbo.CallDispatchGroups", "DepartmentGroupId", "dbo.DepartmentGroups");
            DropForeignKey("dbo.CallDispatchGroups", "CallId", "dbo.Calls");
            DropIndex("dbo.CallUnits", new[] { "UnitStateId" });
            DropIndex("dbo.CallUnits", new[] { "UnitId" });
            DropIndex("dbo.CallUnits", new[] { "CallId" });
            DropIndex("dbo.CallDispatchGroups", new[] { "DepartmentGroupId" });
            DropIndex("dbo.CallDispatchGroups", new[] { "CallId" });
            DropTable("dbo.CallUnits");
            DropTable("dbo.CallDispatchGroups");
        }
    }
}
