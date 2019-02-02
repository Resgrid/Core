namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMessageRecipientsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.MessageRecipients",
                c => new
                    {
                        MessageRecipientId = c.Int(nullable: false, identity: true),
                        UserId = c.Guid(nullable: false),
                        IsDeleted = c.Boolean(nullable: false),
                        ReadOn = c.DateTime(),
                        Response = c.String(),
                        Note = c.String(),
                        Latitude = c.Decimal(precision: 18, scale: 2),
                        Longitude = c.Decimal(precision: 18, scale: 2),
                        Message_MessageId = c.Int(),
                    })
                .PrimaryKey(t => t.MessageRecipientId)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .ForeignKey("dbo.Messages", t => t.Message_MessageId)
                .Index(t => t.UserId)
                .Index(t => t.Message_MessageId);
            
            AddColumn("dbo.Messages", "Type", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.MessageRecipients", "Message_MessageId", "dbo.Messages");
            DropForeignKey("dbo.MessageRecipients", "UserId", "dbo.Users");
            DropIndex("dbo.MessageRecipients", new[] { "Message_MessageId" });
            DropIndex("dbo.MessageRecipients", new[] { "UserId" });
            DropColumn("dbo.Messages", "Type");
            DropTable("dbo.MessageRecipients");
        }
    }
}
