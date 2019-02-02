namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedStartTimeToShift : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Shifts", "StartTime", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Shifts", "StartTime");
        }
    }
}
