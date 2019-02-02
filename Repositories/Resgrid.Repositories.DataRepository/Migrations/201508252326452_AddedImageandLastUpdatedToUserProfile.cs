namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedImageandLastUpdatedToUserProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "Image", c => c.Binary());
            AddColumn("dbo.UserProfiles", "LastUpdated", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "LastUpdated");
            DropColumn("dbo.UserProfiles", "Image");
        }
    }
}
