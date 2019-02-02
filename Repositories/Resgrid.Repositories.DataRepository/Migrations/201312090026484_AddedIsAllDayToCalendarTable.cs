namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedIsAllDayToCalendarTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CalendarItems", "IsAllDay", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.CalendarItems", "IsAllDay");
        }
    }
}
