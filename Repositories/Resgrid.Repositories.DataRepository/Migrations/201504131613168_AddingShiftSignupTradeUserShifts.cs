namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingShiftSignupTradeUserShifts : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftSignupTradeUserShifts",
                c => new
                    {
                        ShiftSignupTradeUserShiftId = c.Int(nullable: false, identity: true),
                        ShiftSignupTradeUserId = c.Int(nullable: false),
                        ShiftSignupId = c.Int(),
                    })
                .PrimaryKey(t => t.ShiftSignupTradeUserShiftId)
                .ForeignKey("dbo.ShiftSignups", t => t.ShiftSignupId)
                .ForeignKey("dbo.ShiftSignupTradeUsers", t => t.ShiftSignupTradeUserId, cascadeDelete: true)
                .Index(t => t.ShiftSignupTradeUserId)
                .Index(t => t.ShiftSignupId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftSignupTradeUserShifts", "ShiftSignupTradeUserId", "dbo.ShiftSignupTradeUsers");
            DropForeignKey("dbo.ShiftSignupTradeUserShifts", "ShiftSignupId", "dbo.ShiftSignups");
            DropIndex("dbo.ShiftSignupTradeUserShifts", new[] { "ShiftSignupId" });
            DropIndex("dbo.ShiftSignupTradeUserShifts", new[] { "ShiftSignupTradeUserId" });
            DropTable("dbo.ShiftSignupTradeUserShifts");
        }
    }
}
