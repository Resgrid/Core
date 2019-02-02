namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AdjustedShiftGroupAssignmentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ShiftGroupAssignments", "Timestamp", c => c.DateTime(nullable: false));
            AddColumn("dbo.ShiftGroupAssignments", "AssignedByUserId", c => c.Guid(nullable: false));
            AlterColumn("dbo.ShiftGroupAssignments", "ShiftDay", c => c.DateTime(nullable: false));
            DropColumn("dbo.ShiftGroupAssignments", "SignupTimestamp");
        }
        
        public override void Down()
        {
            AddColumn("dbo.ShiftGroupAssignments", "SignupTimestamp", c => c.DateTime());
            AlterColumn("dbo.ShiftGroupAssignments", "ShiftDay", c => c.DateTime());
            DropColumn("dbo.ShiftGroupAssignments", "AssignedByUserId");
            DropColumn("dbo.ShiftGroupAssignments", "Timestamp");
        }
    }
}
