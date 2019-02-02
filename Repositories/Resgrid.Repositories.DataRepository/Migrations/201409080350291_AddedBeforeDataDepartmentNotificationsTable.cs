namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedBeforeDataDepartmentNotificationsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentNotifications", "BeforeData", c => c.String());
            AddColumn("dbo.DepartmentNotifications", "CurrentData", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentNotifications", "CurrentData");
            DropColumn("dbo.DepartmentNotifications", "BeforeData");
        }
    }
}
