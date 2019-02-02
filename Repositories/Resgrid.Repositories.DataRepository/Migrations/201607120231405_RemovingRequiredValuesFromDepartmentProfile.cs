namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovingRequiredValuesFromDepartmentProfile : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.DepartmentProfiles", "Description", c => c.String());
            AlterColumn("dbo.DepartmentProfiles", "Founded", c => c.DateTime());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.DepartmentProfiles", "Founded", c => c.DateTime(nullable: false));
            AlterColumn("dbo.DepartmentProfiles", "Description", c => c.String(nullable: false));
        }
    }
}
