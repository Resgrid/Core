namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNotificaitonOptionsToTheUserProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "SendNotificationEmail", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "SendNotificationPush", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "SendNotificationSms", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "SendNotificationSms");
            DropColumn("dbo.UserProfiles", "SendNotificationPush");
            DropColumn("dbo.UserProfiles", "SendNotificationEmail");
        }
    }
}
