namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentProfileTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentProfileArticles",
                c => new
                    {
                        DepartmentProfileArticleId = c.Int(nullable: false, identity: true),
                        DepartmentProfileId = c.Int(nullable: false),
                        Title = c.String(nullable: false),
                        Body = c.String(nullable: false),
                        SmallImage = c.Binary(),
                        LargeImage = c.Binary(),
                        CreatedOn = c.DateTime(nullable: false),
                        ExpiresOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.DepartmentProfileArticleId)
                .ForeignKey("dbo.DepartmentProfiles", t => t.DepartmentProfileId, cascadeDelete: true)
                .Index(t => t.DepartmentProfileId);
            
            CreateTable(
                "dbo.DepartmentProfiles",
                c => new
                    {
                        DepartmentProfileId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false),
                        Code = c.String(nullable: false),
                        ShortName = c.String(),
                        Description = c.String(nullable: false),
                        InCaseOfEmergency = c.String(),
                        ServiceArea = c.String(),
                        ServicesProvided = c.String(),
                        Founded = c.DateTime(nullable: false),
                        Logo = c.Binary(),
                        Keywords = c.String(),
                        InviteOnly = c.Boolean(nullable: false),
                        AllowMessages = c.Boolean(nullable: false),
                        VolunteerPositionsAvailable = c.Boolean(nullable: false),
                        ShareStats = c.Boolean(nullable: false),
                        VolunteerKeywords = c.Boolean(nullable: false),
                        VolunteerDescription = c.Boolean(nullable: false),
                        VolunteerContactName = c.Boolean(nullable: false),
                        VolunteerContactInfo = c.Boolean(nullable: false),
                        Geofence = c.String(),
                        AddressId = c.Int(),
                        Latitude = c.String(),
                        Longitude = c.String(),
                        What3Words = c.String(),
                    })
                .PrimaryKey(t => t.DepartmentProfileId)
                .ForeignKey("dbo.Addresses", t => t.AddressId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.AddressId);
            
            CreateTable(
                "dbo.DepartmentProfileInvites",
                c => new
                    {
                        DepartmentProfileInviteId = c.Int(nullable: false, identity: true),
                        DepartmentProfileId = c.Int(nullable: false),
                        Code = c.String(),
                        UsedOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.DepartmentProfileInviteId)
                .ForeignKey("dbo.DepartmentProfiles", t => t.DepartmentProfileId, cascadeDelete: true)
                .Index(t => t.DepartmentProfileId);
            
            CreateTable(
                "dbo.DepartmentProfileMessages",
                c => new
                    {
                        DepartmentProfileMessageId = c.Int(nullable: false, identity: true),
                        DepartmentProfileId = c.Int(nullable: false),
                        UserId = c.String(),
                        Name = c.String(nullable: false),
                        ContactInfo = c.String(nullable: false),
                        Message = c.String(nullable: false),
                        SentOn = c.DateTime(nullable: false),
                        Image = c.Binary(),
                        Latitude = c.String(),
                        Longitude = c.String(),
                        Response = c.String(),
                        Closed = c.Boolean(nullable: false),
                        ConvertedToCall = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentProfileMessageId)
                .ForeignKey("dbo.DepartmentProfiles", t => t.DepartmentProfileId, cascadeDelete: true)
                .Index(t => t.DepartmentProfileId);
            
            AddColumn("dbo.CalendarItems", "Public", c => c.Boolean(nullable: false));
            AddColumn("dbo.Calls", "Public", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentProfileMessages", "DepartmentProfileId", "dbo.DepartmentProfiles");
            DropForeignKey("dbo.DepartmentProfileInvites", "DepartmentProfileId", "dbo.DepartmentProfiles");
            DropForeignKey("dbo.DepartmentProfileArticles", "DepartmentProfileId", "dbo.DepartmentProfiles");
            DropForeignKey("dbo.DepartmentProfiles", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.DepartmentProfiles", "AddressId", "dbo.Addresses");
            DropIndex("dbo.DepartmentProfileMessages", new[] { "DepartmentProfileId" });
            DropIndex("dbo.DepartmentProfileInvites", new[] { "DepartmentProfileId" });
            DropIndex("dbo.DepartmentProfiles", new[] { "AddressId" });
            DropIndex("dbo.DepartmentProfiles", new[] { "DepartmentId" });
            DropIndex("dbo.DepartmentProfileArticles", new[] { "DepartmentProfileId" });
            DropColumn("dbo.Calls", "Public");
            DropColumn("dbo.CalendarItems", "Public");
            DropTable("dbo.DepartmentProfileMessages");
            DropTable("dbo.DepartmentProfileInvites");
            DropTable("dbo.DepartmentProfiles");
            DropTable("dbo.DepartmentProfileArticles");
        }
    }
}
