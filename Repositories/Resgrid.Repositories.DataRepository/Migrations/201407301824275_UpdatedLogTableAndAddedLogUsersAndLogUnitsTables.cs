namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedLogTableAndAddedLogUsersAndLogUnitsTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.LogUnits",
                c => new
                    {
                        LogUnitId = c.Int(nullable: false, identity: true),
                        LogId = c.Int(nullable: false),
                        UnitId = c.Int(nullable: false),
                        Dispatched = c.DateTime(),
                        Enroute = c.DateTime(),
                        OnScene = c.DateTime(),
                        Released = c.DateTime(),
                        InQuarters = c.DateTime(),
                    })
                .PrimaryKey(t => t.LogUnitId)
                .ForeignKey("dbo.Logs", t => t.LogId, cascadeDelete: true)
                .ForeignKey("dbo.Units", t => t.UnitId, cascadeDelete: true)
                .Index(t => t.LogId)
                .Index(t => t.UnitId);
            
            CreateTable(
                "dbo.LogUsers",
                c => new
                    {
                        LogUserId = c.Int(nullable: false, identity: true),
                        LogId = c.Int(nullable: false),
                        UnitId = c.Int(),
                        UserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.LogUserId)
                .ForeignKey("dbo.Logs", t => t.LogId, cascadeDelete: true)
                .ForeignKey("dbo.Units", t => t.UnitId)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.LogId)
                .Index(t => t.UnitId)
                .Index(t => t.UserId);
            
            AddColumn("dbo.Logs", "LogType", c => c.Int());
            AddColumn("dbo.Logs", "ExternalId", c => c.String());
            AddColumn("dbo.Logs", "InitialReport", c => c.String());
            AddColumn("dbo.Logs", "Type", c => c.String());
            AddColumn("dbo.Logs", "StationGroupId", c => c.Int());
            AddColumn("dbo.Logs", "Course", c => c.String());
            AddColumn("dbo.Logs", "CourseCode", c => c.String());
            AddColumn("dbo.Logs", "Instructors", c => c.String());
            AddColumn("dbo.Logs", "Cause", c => c.String());
            AddColumn("dbo.Logs", "InvestigatedByUserId", c => c.Guid());
            AddColumn("dbo.Logs", "ContactName", c => c.String());
            AddColumn("dbo.Logs", "ContactNumber", c => c.String());
            AddColumn("dbo.Logs", "OfficerUserId", c => c.Guid());
            CreateIndex("dbo.Logs", "StationGroupId");
            CreateIndex("dbo.Logs", "InvestigatedByUserId");
            CreateIndex("dbo.Logs", "OfficerUserId");
            AddForeignKey("dbo.Logs", "InvestigatedByUserId", "dbo.Users", "UserId");
            AddForeignKey("dbo.Logs", "OfficerUserId", "dbo.Users", "UserId");
            AddForeignKey("dbo.Logs", "StationGroupId", "dbo.DepartmentGroups", "DepartmentGroupId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.LogUsers", "UserId", "dbo.Users");
            DropForeignKey("dbo.LogUsers", "UnitId", "dbo.Units");
            DropForeignKey("dbo.LogUsers", "LogId", "dbo.Logs");
            DropForeignKey("dbo.LogUnits", "UnitId", "dbo.Units");
            DropForeignKey("dbo.LogUnits", "LogId", "dbo.Logs");
            DropForeignKey("dbo.Logs", "StationGroupId", "dbo.DepartmentGroups");
            DropForeignKey("dbo.Logs", "OfficerUserId", "dbo.Users");
            DropForeignKey("dbo.Logs", "InvestigatedByUserId", "dbo.Users");
            DropIndex("dbo.LogUsers", new[] { "UserId" });
            DropIndex("dbo.LogUsers", new[] { "UnitId" });
            DropIndex("dbo.LogUsers", new[] { "LogId" });
            DropIndex("dbo.LogUnits", new[] { "UnitId" });
            DropIndex("dbo.LogUnits", new[] { "LogId" });
            DropIndex("dbo.Logs", new[] { "OfficerUserId" });
            DropIndex("dbo.Logs", new[] { "InvestigatedByUserId" });
            DropIndex("dbo.Logs", new[] { "StationGroupId" });
            DropColumn("dbo.Logs", "OfficerUserId");
            DropColumn("dbo.Logs", "ContactNumber");
            DropColumn("dbo.Logs", "ContactName");
            DropColumn("dbo.Logs", "InvestigatedByUserId");
            DropColumn("dbo.Logs", "Cause");
            DropColumn("dbo.Logs", "Instructors");
            DropColumn("dbo.Logs", "CourseCode");
            DropColumn("dbo.Logs", "Course");
            DropColumn("dbo.Logs", "StationGroupId");
            DropColumn("dbo.Logs", "Type");
            DropColumn("dbo.Logs", "InitialReport");
            DropColumn("dbo.Logs", "ExternalId");
            DropColumn("dbo.Logs", "LogType");
            DropTable("dbo.LogUsers");
            DropTable("dbo.LogUnits");
        }
    }
}
