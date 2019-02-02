namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddCallDispatchRoleTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallDispatchRoles",
                c => new
                    {
                        CallDispatchRoleId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        RoleId = c.Int(nullable: false),
                        DispatchCount = c.Int(nullable: false),
                        LastDispatchedOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.CallDispatchRoleId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.PersonnelRoles", t => t.RoleId)
                .Index(t => t.CallId)
                .Index(t => t.RoleId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallDispatchRoles", "RoleId", "dbo.PersonnelRoles");
            DropForeignKey("dbo.CallDispatchRoles", "CallId", "dbo.Calls");
            DropIndex("dbo.CallDispatchRoles", new[] { "RoleId" });
            DropIndex("dbo.CallDispatchRoles", new[] { "CallId" });
            DropTable("dbo.CallDispatchRoles");
        }
    }
}
