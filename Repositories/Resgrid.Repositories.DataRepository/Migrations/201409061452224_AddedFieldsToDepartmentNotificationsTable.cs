namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFieldsToDepartmentNotificationsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentNotifications", "SelectedGroupsAdminsOnly", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentNotifications", "DepartmentAdmins", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentNotifications", "DepartmentAdmins");
            DropColumn("dbo.DepartmentNotifications", "SelectedGroupsAdminsOnly");
        }
    }
}
