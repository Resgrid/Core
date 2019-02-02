namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedValuesToDepartmentCallPriorities : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.DepartmentCallPriorities", "PushNotificationSound", c => c.Binary());
            AddColumn("dbo.DepartmentCallPriorities", "ShortNotificationSound", c => c.Binary());
            AddColumn("dbo.DepartmentCallPriorities", "DispatchPersonnel", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentCallPriorities", "DispatchUnits", c => c.Boolean(nullable: false));
            AddColumn("dbo.DepartmentCallPriorities", "ForceNotifyAllPersonnel", c => c.Boolean(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.DepartmentCallPriorities", "ForceNotifyAllPersonnel");
            DropColumn("dbo.DepartmentCallPriorities", "DispatchUnits");
            DropColumn("dbo.DepartmentCallPriorities", "DispatchPersonnel");
            DropColumn("dbo.DepartmentCallPriorities", "ShortNotificationSound");
            DropColumn("dbo.DepartmentCallPriorities", "PushNotificationSound");
        }
    }
}
