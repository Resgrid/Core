namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MakingVolunteerInfoStringsDepartmentProfile : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.DepartmentProfiles", "VolunteerKeywords", c => c.String());
            AlterColumn("dbo.DepartmentProfiles", "VolunteerDescription", c => c.String());
            AlterColumn("dbo.DepartmentProfiles", "VolunteerContactName", c => c.String());
            AlterColumn("dbo.DepartmentProfiles", "VolunteerContactInfo", c => c.String());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.DepartmentProfiles", "VolunteerContactInfo", c => c.Boolean(nullable: false));
            AlterColumn("dbo.DepartmentProfiles", "VolunteerContactName", c => c.Boolean(nullable: false));
            AlterColumn("dbo.DepartmentProfiles", "VolunteerDescription", c => c.Boolean(nullable: false));
            AlterColumn("dbo.DepartmentProfiles", "VolunteerKeywords", c => c.Boolean(nullable: false));
        }
    }
}
