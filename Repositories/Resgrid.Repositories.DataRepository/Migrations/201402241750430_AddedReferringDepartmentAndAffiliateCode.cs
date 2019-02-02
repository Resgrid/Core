namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedReferringDepartmentAndAffiliateCode : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "ReferringDepartmentId", c => c.Int());
            AddColumn("dbo.Departments", "AffiliateCode", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Departments", "AffiliateCode");
            DropColumn("dbo.Departments", "ReferringDepartmentId");
        }
    }
}
