namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPropertiesToCalandarItemsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CalendarItems", "Location", c => c.String());
            AddColumn("dbo.CalendarItems", "SignupType", c => c.Int(nullable: false));
            AddColumn("dbo.CalendarItems", "Reminder", c => c.Int(nullable: false));
            AddColumn("dbo.CalendarItems", "LockEditing", c => c.Boolean(nullable: false));
            AddColumn("dbo.CalendarItems", "Entities", c => c.String());
            AddColumn("dbo.CalendarItems", "RequiredAttendes", c => c.String());
            AddColumn("dbo.CalendarItems", "OptionalAttendes", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CalendarItems", "OptionalAttendes");
            DropColumn("dbo.CalendarItems", "RequiredAttendes");
            DropColumn("dbo.CalendarItems", "Entities");
            DropColumn("dbo.CalendarItems", "LockEditing");
            DropColumn("dbo.CalendarItems", "Reminder");
            DropColumn("dbo.CalendarItems", "SignupType");
            DropColumn("dbo.CalendarItems", "Location");
        }
    }
}
