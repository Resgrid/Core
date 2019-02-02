namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedStartAndEndDateToUserProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "StartDate", c => c.DateTime());
            AddColumn("dbo.UserProfiles", "EndDate", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "EndDate");
            DropColumn("dbo.UserProfiles", "StartDate");
        }
    }
}
