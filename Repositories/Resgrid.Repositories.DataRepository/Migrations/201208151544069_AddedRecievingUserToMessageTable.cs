namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedRecievingUserToMessageTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("Messages", "ReceivingUserId", c => c.Guid(nullable: false));
            AddForeignKey("Messages", "ReceivingUserId", "Users", "UserId", cascadeDelete: true);
            CreateIndex("Messages", "ReceivingUserId");
        }
        
        public override void Down()
        {
            DropIndex("Messages", new[] { "ReceivingUserId" });
            DropForeignKey("Messages", "ReceivingUserId", "Users");
            DropColumn("Messages", "ReceivingUserId");
        }
    }
}
