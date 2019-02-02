namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMessageIdToRecipientTable : DbMigration
    {
        public override void Up()
        {
					AddColumn("dbo.MessageRecipients", "MessageId", c => c.Int(nullable: false));
					AddForeignKey("MessageRecipients", "MessageId", "Messages", "MessageId");
        }
        
        public override void Down()
        {
					DropForeignKey("dbo.MessageRecipients", "MessageId", "Messages");
					DropColumn("dbo.MessageRecipients", "MessageId");
        }
    }
}
