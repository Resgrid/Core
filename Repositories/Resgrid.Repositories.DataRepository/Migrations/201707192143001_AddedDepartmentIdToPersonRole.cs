namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentIdToPersonRole : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PersonnelRoleUsers", "DepartmentId", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.PersonnelRoleUsers", "DepartmentId");
        }
    }
}
