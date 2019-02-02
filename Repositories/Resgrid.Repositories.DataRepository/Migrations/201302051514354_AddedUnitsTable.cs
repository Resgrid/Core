namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Units",
                c => new
                    {
                        UnitId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false),
                        Type = c.String(),
                    })
                .PrimaryKey(t => t.UnitId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.Units", new[] { "DepartmentId" });
            DropForeignKey("dbo.Units", "DepartmentId", "dbo.Departments");
            DropTable("dbo.Units");
        }
    }
}
