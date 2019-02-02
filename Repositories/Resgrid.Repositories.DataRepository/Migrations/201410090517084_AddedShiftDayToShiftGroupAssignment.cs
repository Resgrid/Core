namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedShiftDayToShiftGroupAssignment : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftGroupAssignments", "ShiftDay", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.ShiftGroupAssignments", "ShiftDay");
        }
    }
}
