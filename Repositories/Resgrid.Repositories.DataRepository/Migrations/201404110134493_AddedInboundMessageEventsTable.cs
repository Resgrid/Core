namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedInboundMessageEventsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.InboundMessageEvents",
                c => new
                    {
                        InboundMessageEventId = c.Int(nullable: false, identity: true),
                        MessageType = c.Int(nullable: false),
                        CustomerId = c.String(nullable: false),
                        RecievedOn = c.DateTime(nullable: false),
                        Data = c.String(nullable: false),
                        Type = c.String(),
                        Processed = c.Boolean(),
                    })
                .PrimaryKey(t => t.InboundMessageEventId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.InboundMessageEvents");
        }
    }
}
