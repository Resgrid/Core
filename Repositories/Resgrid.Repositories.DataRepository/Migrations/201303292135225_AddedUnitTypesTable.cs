namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitTypesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UnitTypes",
                c => new
                    {
                        UnitTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.String(nullable: false, maxLength: 100),
                    })
                .PrimaryKey(t => t.UnitTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.UnitTypes", new[] { "DepartmentId" });
            DropForeignKey("dbo.UnitTypes", "DepartmentId", "dbo.Departments");
            DropTable("dbo.UnitTypes");
        }
    }
}
