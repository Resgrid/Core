namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCalendarTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CalendarItems",
                c => new
                    {
                        CalendarItemId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Title = c.String(nullable: false),
                        Start = c.DateTime(nullable: false),
                        End = c.DateTime(nullable: false),
                        StartTimezone = c.String(),
                        EndTimezone = c.String(),
                        Description = c.String(),
                        RecurrenceId = c.String(),
                        RecurrenceRule = c.String(),
                        RecurrenceException = c.String(),
                        ItemType = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.CalendarItemId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
            CreateTable(
                "dbo.CalendarItemTypes",
                c => new
                    {
                        CalendarItemTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false),
                        Color = c.String(),
                    })
                .PrimaryKey(t => t.CalendarItemTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CalendarItemTypes", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.CalendarItems", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.CalendarItemTypes", new[] { "DepartmentId" });
            DropIndex("dbo.CalendarItems", new[] { "DepartmentId" });
            DropTable("dbo.CalendarItemTypes");
            DropTable("dbo.CalendarItems");
        }
    }
}
