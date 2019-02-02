namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPersonnelRoleUsersTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PersonnelRoleUsers",
                c => new
                    {
                        PersonnelRoleUserId = c.Int(nullable: false, identity: true),
                        PersonnelRoleId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.PersonnelRoleUserId)
                .ForeignKey("dbo.PersonnelRoles", t => t.PersonnelRoleId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.PersonnelRoleId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.PersonnelRoleUsers", new[] { "UserId" });
            DropIndex("dbo.PersonnelRoleUsers", new[] { "PersonnelRoleId" });
            DropForeignKey("dbo.PersonnelRoleUsers", "UserId", "dbo.Users");
            DropForeignKey("dbo.PersonnelRoleUsers", "PersonnelRoleId", "dbo.PersonnelRoles");
            DropTable("dbo.PersonnelRoleUsers");
        }
    }
}
