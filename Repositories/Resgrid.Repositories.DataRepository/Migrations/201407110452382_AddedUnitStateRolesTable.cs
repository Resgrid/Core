namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUnitStateRolesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UnitStateRoles",
                c => new
                    {
                        UnitStateRoleId = c.Int(nullable: false, identity: true),
                        UnitStateId = c.Int(nullable: false),
                        Role = c.String(maxLength: 250),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.UnitStateRoleId)
                .ForeignKey("dbo.UnitStates", t => t.UnitStateId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UnitStateId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.UnitStateRoles", "UserId", "dbo.Users");
            DropForeignKey("dbo.UnitStateRoles", "UnitStateId", "dbo.UnitStates");
            DropIndex("dbo.UnitStateRoles", new[] { "UserId" });
            DropIndex("dbo.UnitStateRoles", new[] { "UnitStateId" });
            DropTable("dbo.UnitStateRoles");
        }
    }
}
