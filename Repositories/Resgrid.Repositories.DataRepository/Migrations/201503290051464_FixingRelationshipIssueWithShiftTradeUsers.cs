namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class FixingRelationshipIssueWithShiftTradeUsers : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.ShiftSignupTradeUsers", "UserId", "dbo.Users");
            AddColumn("dbo.ShiftSignupTrades", "UserId", c => c.Guid());
            CreateIndex("dbo.ShiftSignupTrades", "UserId");
            AddForeignKey("dbo.ShiftSignupTrades", "UserId", "dbo.Users", "UserId");
            AddForeignKey("dbo.ShiftSignupTradeUsers", "UserId", "dbo.Users", "UserId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftSignupTradeUsers", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftSignupTrades", "UserId", "dbo.Users");
            DropIndex("dbo.ShiftSignupTrades", new[] { "UserId" });
            DropColumn("dbo.ShiftSignupTrades", "UserId");
            AddForeignKey("dbo.ShiftSignupTradeUsers", "UserId", "dbo.Users", "UserId", cascadeDelete: true);
        }
    }
}
