namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftDaysTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftDays",
                c => new
                    {
                        ShiftDayId = c.Int(nullable: false, identity: true),
                        ShiftId = c.Int(nullable: false),
                        Day = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftDayId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .Index(t => t.ShiftId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftDays", "ShiftId", "dbo.Shifts");
            DropIndex("dbo.ShiftDays", new[] { "ShiftId" });
            DropTable("dbo.ShiftDays");
        }
    }
}
