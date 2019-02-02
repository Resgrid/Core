namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDisabledDataDepartmentNotificationsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentNotifications", "Disabled", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentNotifications", "Data", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentNotifications", "Data");
            DropColumn("dbo.DepartmentNotifications", "Disabled");
        }
    }
}
