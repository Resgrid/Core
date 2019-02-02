namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedUpperAndLowerBoundsToEvents : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentNotifications", "UpperLimit", c => c.Int());
            AddColumn("dbo.DepartmentNotifications", "LowerLimit", c => c.Int());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentNotifications", "LowerLimit");
            DropColumn("dbo.DepartmentNotifications", "UpperLimit");
        }
    }
}
