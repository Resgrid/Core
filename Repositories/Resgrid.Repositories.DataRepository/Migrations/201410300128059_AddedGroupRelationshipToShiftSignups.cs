namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGroupRelationshipToShiftSignups : DbMigration
    {
        public override void Up()
        {
            CreateIndex("dbo.ShiftSignups", "DepartmentGroupId");
            AddForeignKey("dbo.ShiftSignups", "DepartmentGroupId", "dbo.DepartmentGroups", "DepartmentGroupId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftSignups", "DepartmentGroupId", "dbo.DepartmentGroups");
            DropIndex("dbo.ShiftSignups", new[] { "DepartmentGroupId" });
        }
    }
}
