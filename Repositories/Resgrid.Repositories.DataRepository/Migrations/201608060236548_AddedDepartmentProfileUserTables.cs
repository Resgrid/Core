namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentProfileUserTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentProfileUserFollows",
                c => new
                    {
                        DepartmentProfileUserFollowId = c.Int(nullable: false, identity: true),
                        DepartmentProfileUserId = c.Guid(nullable: false),
                        DepartmentProfileId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentProfileUserFollowId)
                .ForeignKey("dbo.DepartmentProfiles", t => t.DepartmentProfileId, cascadeDelete: true)
                .ForeignKey("dbo.DepartmentProfileUsers", t => t.DepartmentProfileUserId, cascadeDelete: true)
                .Index(t => t.DepartmentProfileUserId)
                .Index(t => t.DepartmentProfileId);
            
            CreateTable(
                "dbo.DepartmentProfileUsers",
                c => new
                    {
                        DepartmentProfileUserId = c.Guid(nullable: false, identity: true),
                        Identity = c.String(nullable: false),
                        Name = c.String(nullable: false),
                        Email = c.String(),
                    })
                .PrimaryKey(t => t.DepartmentProfileUserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentProfileUserFollows", "DepartmentProfileUserId", "dbo.DepartmentProfileUsers");
            DropForeignKey("dbo.DepartmentProfileUserFollows", "DepartmentProfileId", "dbo.DepartmentProfiles");
            DropIndex("dbo.DepartmentProfileUserFollows", new[] { "DepartmentProfileId" });
            DropIndex("dbo.DepartmentProfileUserFollows", new[] { "DepartmentProfileUserId" });
            DropTable("dbo.DepartmentProfileUsers");
            DropTable("dbo.DepartmentProfileUserFollows");
        }
    }
}
