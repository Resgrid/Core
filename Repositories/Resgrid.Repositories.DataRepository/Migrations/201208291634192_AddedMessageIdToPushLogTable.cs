namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMessageIdToPushLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PushLogs", "MessageId", c => c.String(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.PushLogs", "MessageId");
        }
    }
}
