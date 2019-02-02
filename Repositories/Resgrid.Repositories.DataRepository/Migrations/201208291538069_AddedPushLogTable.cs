namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPushLogTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.PushLogs",
                c => new
                    {
                        PushLogId = c.Int(nullable: false, identity: true),
                        ChannelUri = c.String(nullable: false),
                        Status = c.Int(nullable: false),
                        Connection = c.Int(nullable: false),
                        Subscription = c.Int(nullable: false),
                        Notification = c.Int(nullable: false),
                        Exception = c.String(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.PushLogId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.PushLogs");
        }
    }
}
