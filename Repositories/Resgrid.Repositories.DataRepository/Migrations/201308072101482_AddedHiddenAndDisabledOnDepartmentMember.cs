namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedHiddenAndDisabledOnDepartmentMember : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentMembers", "IsDisabled", c => c.Boolean());
            AddColumn("dbo.DepartmentMembers", "IsHidden", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentMembers", "IsHidden");
            DropColumn("dbo.DepartmentMembers", "IsDisabled");
        }
    }
}
