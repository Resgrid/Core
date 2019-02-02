namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitToInventoriesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Inventories", "UnitId", c => c.Int());
            CreateIndex("dbo.Inventories", "UnitId");
            AddForeignKey("dbo.Inventories", "UnitId", "dbo.Units", "UnitId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Inventories", "UnitId", "dbo.Units");
            DropIndex("dbo.Inventories", new[] { "UnitId" });
            DropColumn("dbo.Inventories", "UnitId");
        }
    }
}
