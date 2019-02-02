namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedParentDepartmentGroupToDepartmentGroupsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroups", "ParentDepartmentGroupId", c => c.Int());
            AddForeignKey("dbo.DepartmentGroups", "ParentDepartmentGroupId", "dbo.DepartmentGroups", "DepartmentGroupId");
            CreateIndex("dbo.DepartmentGroups", "ParentDepartmentGroupId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.DepartmentGroups", new[] { "ParentDepartmentGroupId" });
            DropForeignKey("dbo.DepartmentGroups", "ParentDepartmentGroupId", "dbo.DepartmentGroups");
            DropColumn("dbo.DepartmentGroups", "ParentDepartmentGroupId");
        }
    }
}
