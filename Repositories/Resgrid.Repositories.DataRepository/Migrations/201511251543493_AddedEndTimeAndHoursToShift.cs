namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedEndTimeAndHoursToShift : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Shifts", "EndTime", c => c.String());
            AddColumn("dbo.Shifts", "Hours", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Shifts", "Hours");
            DropColumn("dbo.Shifts", "EndTime");
        }
    }
}
