namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitRolesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UnitRoles",
                c => new
                    {
                        UnitRoleId = c.Int(nullable: false, identity: true),
                        UnitId = c.Int(nullable: false),
                        Name = c.String(nullable: false, maxLength: 250),
                    })
                .PrimaryKey(t => t.UnitRoleId)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.UnitId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.UnitRoles", "UnitId", "dbo.Units");
            DropIndex("dbo.UnitRoles", new[] { "UnitId" });
            DropTable("dbo.UnitRoles");
        }
    }
}
