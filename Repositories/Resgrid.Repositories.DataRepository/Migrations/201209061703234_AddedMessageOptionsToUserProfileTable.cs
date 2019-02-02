namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMessageOptionsToUserProfileTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "SendMessageEmail", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "SendMessagePush", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "SendMessageSms", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "SendMessageSms");
            DropColumn("dbo.UserProfiles", "SendMessagePush");
            DropColumn("dbo.UserProfiles", "SendMessageEmail");
        }
    }
}
