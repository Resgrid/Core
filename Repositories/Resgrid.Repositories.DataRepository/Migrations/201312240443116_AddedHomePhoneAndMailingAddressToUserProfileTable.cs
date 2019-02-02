namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedHomePhoneAndMailingAddressToUserProfileTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "HomeNumber", c => c.String());
            AddColumn("dbo.UserProfiles", "HomeAddressId", c => c.Int());
            AddColumn("dbo.UserProfiles", "MailingAddressId", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "MailingAddressId");
            DropColumn("dbo.UserProfiles", "HomeAddressId");
            DropColumn("dbo.UserProfiles", "HomeNumber");
        }
    }
}
