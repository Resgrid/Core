namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPropertiesToShiftTableAddedShiftAdminsAndShiftSignupsTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftAdmins",
                c => new
                    {
                        ShiftAdminId = c.Int(nullable: false, identity: true),
                        ShiftId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftAdminId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.ShiftId)
                .Index(t => t.UserId);
            
            CreateTable(
                "dbo.ShiftSignups",
                c => new
                    {
                        ShiftSignupId = c.Int(nullable: false, identity: true),
                        ShiftId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        SignupTimestamp = c.DateTime(nullable: false),
                        ShiftDay = c.DateTime(nullable: false),
                        Denied = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftSignupId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.ShiftId)
                .Index(t => t.UserId);
            
            AddColumn("dbo.Shifts", "AllowPartials", c => c.Boolean());
            AddColumn("dbo.Shifts", "RequireApproval", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftAdmins", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftAdmins", "ShiftId", "dbo.Shifts");
            DropForeignKey("dbo.ShiftSignups", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftSignups", "ShiftId", "dbo.Shifts");
            DropIndex("dbo.ShiftSignups", new[] { "UserId" });
            DropIndex("dbo.ShiftSignups", new[] { "ShiftId" });
            DropIndex("dbo.ShiftAdmins", new[] { "UserId" });
            DropIndex("dbo.ShiftAdmins", new[] { "ShiftId" });
            DropColumn("dbo.Shifts", "RequireApproval");
            DropColumn("dbo.Shifts", "AllowPartials");
            DropTable("dbo.ShiftSignups");
            DropTable("dbo.ShiftAdmins");
        }
    }
}
