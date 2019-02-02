namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMoreValuesToDepartmentProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentProfiles", "Facebook", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "Twitter", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "GooglePlus", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "LinkedIn", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "Instagram", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "YouTube", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "Website", c => c.String());
            AddColumn("dbo.DepartmentProfiles", "PhoneNumber", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentProfiles", "PhoneNumber");
            DropColumn("dbo.DepartmentProfiles", "Website");
            DropColumn("dbo.DepartmentProfiles", "YouTube");
            DropColumn("dbo.DepartmentProfiles", "Instagram");
            DropColumn("dbo.DepartmentProfiles", "LinkedIn");
            DropColumn("dbo.DepartmentProfiles", "GooglePlus");
            DropColumn("dbo.DepartmentProfiles", "Twitter");
            DropColumn("dbo.DepartmentProfiles", "Facebook");
        }
    }
}
