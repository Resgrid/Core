namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCreateByUserToCalendarItems : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CalendarItems", "CreatorUserId", c => c.Guid());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CalendarItems", "CreatorUserId");
        }
    }
}
