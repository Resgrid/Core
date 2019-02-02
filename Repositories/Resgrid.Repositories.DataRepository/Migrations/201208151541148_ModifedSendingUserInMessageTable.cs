namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class ModifedSendingUserInMessageTable : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("Messages", "SendingUserId", "Users");
            DropIndex("Messages", new[] { "SendingUserId" });
            AlterColumn("Messages", "SendingUserId", c => c.Guid());
            AddForeignKey("Messages", "SendingUserId", "Users", "UserId");
            CreateIndex("Messages", "SendingUserId");
        }
        
        public override void Down()
        {
            DropIndex("Messages", new[] { "SendingUserId" });
            DropForeignKey("Messages", "SendingUserId", "Users");
            AlterColumn("Messages", "SendingUserId", c => c.Guid(nullable: false));
            CreateIndex("Messages", "SendingUserId");
            AddForeignKey("Messages", "SendingUserId", "Users", "UserId", cascadeDelete: true);
        }
    }
}
