namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedInventoriesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Inventories",
                c => new
                    {
                        InventoryId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        GroupId = c.Int(nullable: false),
                        TypeId = c.Int(nullable: false),
                        Batch = c.String(),
                        Note = c.String(),
                        Amount = c.Double(nullable: false),
                        TimeStamp = c.DateTime(nullable: false),
                        AddedByUserId = c.Guid(nullable: false),
                        InventoryType_InventoryTypeId = c.Int(),
                    })
                .PrimaryKey(t => t.InventoryId)
                .ForeignKey("dbo.Users", t => t.AddedByUserId, cascadeDelete: true)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.DepartmentGroups", t => t.GroupId)
                .ForeignKey("dbo.InventoryTypes", t => t.InventoryType_InventoryTypeId)
                .ForeignKey("dbo.InventoryTypes", t => t.TypeId)
                .Index(t => t.DepartmentId)
                .Index(t => t.GroupId)
                .Index(t => t.TypeId)
                .Index(t => t.AddedByUserId)
                .Index(t => t.InventoryType_InventoryTypeId);
            
            AddColumn("dbo.InventoryTypes", "ExpiresDays", c => c.Int());
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Inventories", "TypeId", "dbo.InventoryTypes");
            DropForeignKey("dbo.Inventories", "InventoryType_InventoryTypeId", "dbo.InventoryTypes");
            DropForeignKey("dbo.Inventories", "GroupId", "dbo.DepartmentGroups");
            DropForeignKey("dbo.Inventories", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.Inventories", "AddedByUserId", "dbo.Users");
            DropIndex("dbo.Inventories", new[] { "InventoryType_InventoryTypeId" });
            DropIndex("dbo.Inventories", new[] { "AddedByUserId" });
            DropIndex("dbo.Inventories", new[] { "TypeId" });
            DropIndex("dbo.Inventories", new[] { "GroupId" });
            DropIndex("dbo.Inventories", new[] { "DepartmentId" });
            DropColumn("dbo.InventoryTypes", "ExpiresDays");
            DropTable("dbo.Inventories");
        }
    }
}
