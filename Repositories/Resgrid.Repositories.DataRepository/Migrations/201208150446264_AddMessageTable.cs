namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddMessageTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "Messages",
                c => new
                    {
                        MessageId = c.Int(nullable: false, identity: true),
                        Subject = c.String(nullable: false, maxLength: 150),
                        IsBroadcast = c.Boolean(nullable: false),
                        SendingUserId = c.Guid(nullable: false),
                        Body = c.String(nullable: false, maxLength: 500),
                        SentOn = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.MessageId)
                .ForeignKey("Users", t => t.SendingUserId, cascadeDelete: true)
                .Index(t => t.SendingUserId);
            
        }
        
        public override void Down()
        {
            DropIndex("Messages", new[] { "SendingUserId" });
            DropForeignKey("Messages", "SendingUserId", "Users");
            DropTable("Messages");
        }
    }
}
