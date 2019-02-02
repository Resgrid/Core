namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftPeronsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftPersons",
                c => new
                    {
                        ShiftPersonId = c.Int(nullable: false, identity: true),
                        ShiftId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftPersonId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.ShiftId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftPersons", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftPersons", "ShiftId", "dbo.Shifts");
            DropIndex("dbo.ShiftPersons", new[] { "UserId" });
            DropIndex("dbo.ShiftPersons", new[] { "ShiftId" });
            DropTable("dbo.ShiftPersons");
        }
    }
}
