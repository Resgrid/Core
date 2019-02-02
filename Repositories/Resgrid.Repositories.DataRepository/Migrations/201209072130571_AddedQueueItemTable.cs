namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedQueueItemTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.QueueItems",
                c => new
                    {
                        QueueItemId = c.Int(nullable: false, identity: true),
                        QueueType = c.Int(nullable: false),
                        SourceId = c.String(),
                        QueuedOn = c.DateTime(nullable: false),
                        PickedUp = c.DateTime(),
                        CompletedOn = c.DateTime(),
                        Receipt = c.String(),
                    })
                .PrimaryKey(t => t.QueueItemId);
        }
        
        public override void Down()
        {
            DropTable("dbo.QueueItems");
        }
    }
}
