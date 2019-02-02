namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class FixingDependencyIssueWithDepartmentGroupMember : DbMigration
    {
        public override void Up()
        {
            //DropForeignKey("dbo.DepartmentGroupMembers", "UserId", "dbo.Users");
            DropIndex("dbo.DepartmentGroupMembers", new[] { "UserId" });
            AddForeignKey("dbo.DepartmentGroupMembers", "UserId", "dbo.Users", "UserId", cascadeDelete: true);
            CreateIndex("dbo.DepartmentGroupMembers", "UserId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.DepartmentGroupMembers", new[] { "UserId" });
            DropForeignKey("dbo.DepartmentGroupMembers", "UserId", "dbo.Users");
            CreateIndex("dbo.DepartmentGroupMembers", "UserId");
            AddForeignKey("dbo.DepartmentGroupMembers", "UserId", "dbo.Users", "UserId");
        }
    }
}
