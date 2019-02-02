namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedReminderSentToCalItemAndNewAttendeesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CalendarItemAttendees",
                c => new
                    {
                        CalendarItemAttendeeId = c.Int(nullable: false, identity: true),
                        CalendarItemId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        AttendeeType = c.Int(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                        Note = c.String(),
                    })
                .PrimaryKey(t => t.CalendarItemAttendeeId)
                .ForeignKey("dbo.CalendarItems", t => t.CalendarItemId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.CalendarItemId)
                .Index(t => t.UserId);
            
            AddColumn("dbo.CalendarItems", "ReminderSent", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CalendarItemAttendees", "UserId", "dbo.Users");
            DropForeignKey("dbo.CalendarItemAttendees", "CalendarItemId", "dbo.CalendarItems");
            DropIndex("dbo.CalendarItemAttendees", new[] { "UserId" });
            DropIndex("dbo.CalendarItemAttendees", new[] { "CalendarItemId" });
            DropColumn("dbo.CalendarItems", "ReminderSent");
            DropTable("dbo.CalendarItemAttendees");
        }
    }
}
