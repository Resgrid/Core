namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddIOSCallPushSound : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentCallPriorities", "IOSPushNotificationSound", c => c.Binary());
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentCallPriorities", "IOSPushNotificationSound");
        }
    }
}
