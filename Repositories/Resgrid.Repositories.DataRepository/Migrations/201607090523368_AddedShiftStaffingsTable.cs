namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftStaffingsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftStaffingPersons",
                c => new
                    {
                        ShiftStaffingPersonId = c.Int(nullable: false, identity: true),
                        ShiftStaffingId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Assigned = c.Boolean(nullable: false),
                        GroupId = c.Int(),
                    })
                .PrimaryKey(t => t.ShiftStaffingPersonId)
                .ForeignKey("dbo.DepartmentGroups", t => t.GroupId)
                .ForeignKey("dbo.ShiftStaffings", t => t.ShiftStaffingId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.ShiftStaffingId)
                .Index(t => t.UserId)
                .Index(t => t.GroupId);
            
            CreateTable(
                "dbo.ShiftStaffings",
                c => new
                    {
                        ShiftStaffingId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        ShiftId = c.Int(nullable: false),
                        ShiftDay = c.DateTime(nullable: false),
                        Note = c.String(),
                        AddedByUserId = c.Guid(nullable: false),
                        AddedOn = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftStaffingId)
                .ForeignKey("dbo.Users", t => t.AddedByUserId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.ShiftId)
                .Index(t => t.AddedByUserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftStaffingPersons", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftStaffingPersons", "ShiftStaffingId", "dbo.ShiftStaffings");
            DropForeignKey("dbo.ShiftStaffings", "ShiftId", "dbo.Shifts");
            DropForeignKey("dbo.ShiftStaffings", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.ShiftStaffings", "AddedByUserId", "dbo.Users");
            DropForeignKey("dbo.ShiftStaffingPersons", "GroupId", "dbo.DepartmentGroups");
            DropIndex("dbo.ShiftStaffings", new[] { "AddedByUserId" });
            DropIndex("dbo.ShiftStaffings", new[] { "ShiftId" });
            DropIndex("dbo.ShiftStaffings", new[] { "DepartmentId" });
            DropIndex("dbo.ShiftStaffingPersons", new[] { "GroupId" });
            DropIndex("dbo.ShiftStaffingPersons", new[] { "UserId" });
            DropIndex("dbo.ShiftStaffingPersons", new[] { "ShiftStaffingId" });
            DropTable("dbo.ShiftStaffings");
            DropTable("dbo.ShiftStaffingPersons");
        }
    }
}
