namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedEveryoneToDepartmentNotificationsTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentNotifications", "Everyone", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentNotifications", "Everyone");
        }
    }
}
