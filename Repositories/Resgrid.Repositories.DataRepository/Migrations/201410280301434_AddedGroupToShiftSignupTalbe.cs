namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedGroupToShiftSignupTalbe : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftSignups", "DepartmentGroupId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftSignups", "DepartmentGroupId");
        }
    }
}
