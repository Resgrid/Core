namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedVoiceOptionsToUserProfile : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.UserProfiles", "VoiceForCall", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "VoiceCallMobile", c => c.Boolean(nullable: false));
            AddColumn("dbo.UserProfiles", "VoiceCallHome", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserProfiles", "VoiceCallHome");
            DropColumn("dbo.UserProfiles", "VoiceCallMobile");
            DropColumn("dbo.UserProfiles", "VoiceForCall");
        }
    }
}
