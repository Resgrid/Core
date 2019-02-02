namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingV2CalendarSupport : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CalendarItems", "IsV2Schedule", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "RecurrenceType", c => c.Int(nullable: false));
            AddColumn("dbo.CalendarItems", "RecurrenceEnd", c => c.DateTime());
            AddColumn("dbo.CalendarItems", "Sunday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Monday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Tuesday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Wednesday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Thursday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Friday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Saturday", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "RepeatOnDay", c => c.Int(nullable: false));
            AddColumn("dbo.CalendarItems", "RepeatOnWeek", c => c.Int(nullable: false));
            AddColumn("dbo.CalendarItems", "RepeatOnMonth", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CalendarItems", "RepeatOnMonth");
            DropColumn("dbo.CalendarItems", "RepeatOnWeek");
            DropColumn("dbo.CalendarItems", "RepeatOnDay");
            DropColumn("dbo.CalendarItems", "Saturday");
            DropColumn("dbo.CalendarItems", "Friday");
            DropColumn("dbo.CalendarItems", "Thursday");
            DropColumn("dbo.CalendarItems", "Wednesday");
            DropColumn("dbo.CalendarItems", "Tuesday");
            DropColumn("dbo.CalendarItems", "Monday");
            DropColumn("dbo.CalendarItems", "Sunday");
            DropColumn("dbo.CalendarItems", "RecurrenceEnd");
            DropColumn("dbo.CalendarItems", "RecurrenceType");
            DropColumn("dbo.CalendarItems", "IsV2Schedule");
        }
    }
}
