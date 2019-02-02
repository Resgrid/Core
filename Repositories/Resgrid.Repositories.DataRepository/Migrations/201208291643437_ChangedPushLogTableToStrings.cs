namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ChangedPushLogTableToStrings : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.PushLogs", "Status", c => c.String(nullable: false));
            AlterColumn("dbo.PushLogs", "Connection", c => c.String(nullable: false));
            AlterColumn("dbo.PushLogs", "Subscription", c => c.String(nullable: false));
            AlterColumn("dbo.PushLogs", "Notification", c => c.String(nullable: false));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.PushLogs", "Notification", c => c.Int(nullable: false));
            AlterColumn("dbo.PushLogs", "Subscription", c => c.Int(nullable: false));
            AlterColumn("dbo.PushLogs", "Connection", c => c.Int(nullable: false));
            AlterColumn("dbo.PushLogs", "Status", c => c.Int(nullable: false));
        }
    }
}
