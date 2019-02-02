namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPushUriTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PushUris",
                c => new
                    {
                        PushUriId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        PlatformType = c.Int(nullable: false),
                        PushLocation = c.String(nullable: false),
                    })
                .PrimaryKey(t => t.PushUriId)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.PushUris", new[] { "UserId" });
            DropForeignKey("dbo.PushUris", "UserId", "dbo.Users");
            DropTable("dbo.PushUris");
        }
    }
}
