namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingDepartmentIdToUserStateAndTasks : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ScheduledTasks", "DepartmentId", c => c.Int(nullable: false));
            AddColumn("dbo.UserStates", "DepartmentId", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.UserStates", "DepartmentId");
            DropColumn("dbo.ScheduledTasks", "DepartmentId");
        }
    }
}
