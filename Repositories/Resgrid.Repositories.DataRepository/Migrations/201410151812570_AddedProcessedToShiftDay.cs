namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedProcessedToShiftDay : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftDays", "Processed", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftDays", "Processed");
        }
    }
}
