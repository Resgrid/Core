namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPOITypesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.POITypes",
                c => new
                    {
                        PoiTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false, maxLength: 100),
                        Image = c.String(),
                        Color = c.String(),
                    })
                .PrimaryKey(t => t.PoiTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.POITypes", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.POITypes", new[] { "DepartmentId" });
            DropTable("dbo.POITypes");
        }
    }
}
