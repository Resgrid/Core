namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedBaseShiftTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ShiftGroupAssignments",
                c => new
                    {
                        ShiftGroupAssignmentId = c.Int(nullable: false, identity: true),
                        ShiftGroupId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Assigned = c.Boolean(nullable: false),
                        SignupTimestamp = c.DateTime(),
                    })
                .PrimaryKey(t => t.ShiftGroupAssignmentId)
                .ForeignKey("dbo.ShiftGroups", t => t.ShiftGroupId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.ShiftGroupId)
                .Index(t => t.UserId);
            
            CreateTable(
                "dbo.ShiftGroups",
                c => new
                    {
                        ShiftGroupId = c.Int(nullable: false, identity: true),
                        ShiftId = c.Int(nullable: false),
                        DepartmentGroupId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftGroupId)
                .ForeignKey("dbo.DepartmentGroups", t => t.DepartmentGroupId)
                .ForeignKey("dbo.Shifts", t => t.ShiftId, cascadeDelete: true)
                .Index(t => t.ShiftId)
                .Index(t => t.DepartmentGroupId);
            
            CreateTable(
                "dbo.Shifts",
                c => new
                    {
                        ShiftId = c.Int(nullable: false, identity: true),
                        Name = c.String(),
                        Code = c.String(),
                        ScheduleType = c.Int(nullable: false),
                        AssignmentType = c.Int(nullable: false),
                        Color = c.String(),
                        StartDay = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftId);
            
            CreateTable(
                "dbo.ShiftGroupRoles",
                c => new
                    {
                        ShiftGroupRoleId = c.Int(nullable: false, identity: true),
                        ShiftGroupId = c.Int(nullable: false),
                        PersonnelRoleId = c.Int(nullable: false),
                        Required = c.Int(nullable: false),
                        Optional = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.ShiftGroupRoleId)
                .ForeignKey("dbo.PersonnelRoles", t => t.PersonnelRoleId, cascadeDelete: true)
                .ForeignKey("dbo.ShiftGroups", t => t.ShiftGroupId, cascadeDelete: true)
                .Index(t => t.ShiftGroupId)
                .Index(t => t.PersonnelRoleId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ShiftGroupRoles", "ShiftGroupId", "dbo.ShiftGroups");
            DropForeignKey("dbo.ShiftGroupRoles", "PersonnelRoleId", "dbo.PersonnelRoles");
            DropForeignKey("dbo.ShiftGroupAssignments", "UserId", "dbo.Users");
            DropForeignKey("dbo.ShiftGroupAssignments", "ShiftGroupId", "dbo.ShiftGroups");
            DropForeignKey("dbo.ShiftGroups", "ShiftId", "dbo.Shifts");
            DropForeignKey("dbo.ShiftGroups", "DepartmentGroupId", "dbo.DepartmentGroups");
            DropIndex("dbo.ShiftGroupRoles", new[] { "PersonnelRoleId" });
            DropIndex("dbo.ShiftGroupRoles", new[] { "ShiftGroupId" });
            DropIndex("dbo.ShiftGroups", new[] { "DepartmentGroupId" });
            DropIndex("dbo.ShiftGroups", new[] { "ShiftId" });
            DropIndex("dbo.ShiftGroupAssignments", new[] { "UserId" });
            DropIndex("dbo.ShiftGroupAssignments", new[] { "ShiftGroupId" });
            DropTable("dbo.ShiftGroupRoles");
            DropTable("dbo.Shifts");
            DropTable("dbo.ShiftGroups");
            DropTable("dbo.ShiftGroupAssignments");
        }
    }
}
