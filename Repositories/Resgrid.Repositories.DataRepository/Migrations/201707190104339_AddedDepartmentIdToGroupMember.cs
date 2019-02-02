namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentIdToGroupMember : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentGroupMembers", "DepartmentId", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentGroupMembers", "DepartmentId");
        }
    }
}
