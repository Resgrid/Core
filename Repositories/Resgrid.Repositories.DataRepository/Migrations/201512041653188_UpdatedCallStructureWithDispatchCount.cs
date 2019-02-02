namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedCallStructureWithDispatchCount : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Calls", "DispatchCount", c => c.Int(nullable: false));
            AddColumn("dbo.Calls", "LastDispatchedOn", c => c.DateTime());
            AddColumn("dbo.CallDispatches", "Dispatched", c => c.Boolean(nullable: false));
            AddColumn("dbo.CallDispatches", "DispatchCount", c => c.Int(nullable: false));
            AddColumn("dbo.CallDispatches", "LastDispatchedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.CallDispatches", "LastDispatchedOn");
            DropColumn("dbo.CallDispatches", "DispatchCount");
            DropColumn("dbo.CallDispatches", "Dispatched");
            DropColumn("dbo.Calls", "LastDispatchedOn");
            DropColumn("dbo.Calls", "DispatchCount");
        }
    }
}
