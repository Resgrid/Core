namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddInventoryTypesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.InventoryTypes",
                c => new
                    {
                        InventoryTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.String(nullable: false, maxLength: 250),
                    })
                .PrimaryKey(t => t.InventoryTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.InventoryTypes", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.InventoryTypes", new[] { "DepartmentId" });
            DropTable("dbo.InventoryTypes");
        }
    }
}
