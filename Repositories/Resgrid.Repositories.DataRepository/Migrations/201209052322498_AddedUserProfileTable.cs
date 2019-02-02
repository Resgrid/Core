namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUserProfileTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.UserProfiles",
                c => new
                    {
                        UserProfileId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        FirstName = c.String(),
                        LastName = c.String(),
                        TimeZone = c.String(),
                        MobileNumber = c.String(),
                        MobileCarrier = c.Int(nullable: false),
                        SendEmail = c.Boolean(nullable: false),
                        SendPush = c.Boolean(nullable: false),
                        SendSms = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.UserProfileId)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.UserProfiles", new[] { "UserId" });
            DropForeignKey("dbo.UserProfiles", "UserId", "dbo.Users");
            DropTable("dbo.UserProfiles");
        }
    }
}
