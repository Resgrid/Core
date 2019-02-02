namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIsDeletedToDepartmentMember : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentMembers", "IsDeleted", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentMembers", "IsDeleted");
        }
    }
}
