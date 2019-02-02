namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MakingRecipientNullable : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.Messages", "ReceivingUserId", "dbo.Users");
            DropIndex("dbo.Messages", new[] { "ReceivingUserId" });
            AlterColumn("dbo.Messages", "ReceivingUserId", c => c.Guid());
            CreateIndex("dbo.Messages", "ReceivingUserId");
            AddForeignKey("dbo.Messages", "ReceivingUserId", "dbo.Users", "UserId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Messages", "ReceivingUserId", "dbo.Users");
            DropIndex("dbo.Messages", new[] { "ReceivingUserId" });
            AlterColumn("dbo.Messages", "ReceivingUserId", c => c.Guid(nullable: false));
            CreateIndex("dbo.Messages", "ReceivingUserId");
            AddForeignKey("dbo.Messages", "ReceivingUserId", "dbo.Users", "UserId", cascadeDelete: true);
        }
    }
}
