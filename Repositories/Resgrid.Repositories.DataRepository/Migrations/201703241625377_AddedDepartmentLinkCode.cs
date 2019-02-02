namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentLinkCode : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "LinkCode", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Departments", "LinkCode");
        }
    }
}
