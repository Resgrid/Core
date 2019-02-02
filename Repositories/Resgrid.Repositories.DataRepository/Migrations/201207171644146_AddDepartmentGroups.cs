namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddDepartmentGroups : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "DepartmentGroups",
                c => new
                    {
                        DepartmentGroupId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentGroupId)
                .ForeignKey("Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
            CreateTable(
                "DepartmentGroupMembers",
                c => new
                    {
                        DepartmentGroupMemberId = c.Int(nullable: false, identity: true),
                        DepartmentGroupId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentGroupMemberId)
                .ForeignKey("DepartmentGroups", t => t.DepartmentGroupId, cascadeDelete: false)
                .ForeignKey("Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.DepartmentGroupId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("DepartmentGroupMembers", new[] { "UserId" });
            DropIndex("DepartmentGroupMembers", new[] { "DepartmentGroupId" });
            DropIndex("DepartmentGroups", new[] { "DepartmentId" });
            DropForeignKey("DepartmentGroupMembers", "UserId", "Users");
            DropForeignKey("DepartmentGroupMembers", "DepartmentGroupId", "DepartmentGroups");
            DropForeignKey("DepartmentGroups", "DepartmentId", "Departments");
            DropTable("DepartmentGroupMembers");
            DropTable("DepartmentGroups");
        }
    }
}
