namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftSignupTradesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftSignupTrades",
                c => new
                    {
                        ShiftSignupTradeId = c.Int(nullable: false, identity: true),
                        SourceShiftSignupId = c.Int(nullable: false),
                        TargetShiftSignupId = c.Int(),
                        Denied = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftSignupTradeId)
                .ForeignKey("dbo.ShiftSignups", t => t.SourceShiftSignupId, cascadeDelete: true)
                .ForeignKey("dbo.ShiftSignups", t => t.TargetShiftSignupId)
                .Index(t => t.SourceShiftSignupId)
                .Index(t => t.TargetShiftSignupId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftSignupTrades", "TargetShiftSignupId", "dbo.ShiftSignups");
            DropForeignKey("dbo.ShiftSignupTrades", "SourceShiftSignupId", "dbo.ShiftSignups");
            DropIndex("dbo.ShiftSignupTrades", new[] { "TargetShiftSignupId" });
            DropIndex("dbo.ShiftSignupTrades", new[] { "SourceShiftSignupId" });
            DropTable("dbo.ShiftSignupTrades");
        }
    }
}
