namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGroupToShiftPersonTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftPersons", "GroupId", c => c.Int());
            CreateIndex("dbo.ShiftPersons", "GroupId");
            AddForeignKey("dbo.ShiftPersons", "GroupId", "dbo.DepartmentGroups", "DepartmentGroupId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftPersons", "GroupId", "dbo.DepartmentGroups");
            DropIndex("dbo.ShiftPersons", new[] { "GroupId" });
            DropColumn("dbo.ShiftPersons", "GroupId");
        }
    }
}
