namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftSignupTradeUsersTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftSignupTradeUsers",
                c => new
                    {
                        ShiftSignupTradeUserId = c.Int(nullable: false, identity: true),
                        ShiftSignupTradeId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftSignupTradeUserId)
                .ForeignKey("dbo.ShiftSignupTrades", t => t.ShiftSignupTradeId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: false)
                .Index(t => t.ShiftSignupTradeId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftSignupTradeUsers", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftSignupTradeUsers", "ShiftSignupTradeId", "dbo.ShiftSignupTrades");
            DropIndex("dbo.ShiftSignupTradeUsers", new[] { "UserId" });
            DropIndex("dbo.ShiftSignupTradeUsers", new[] { "ShiftSignupTradeId" });
            DropTable("dbo.ShiftSignupTradeUsers");
        }
    }
}
