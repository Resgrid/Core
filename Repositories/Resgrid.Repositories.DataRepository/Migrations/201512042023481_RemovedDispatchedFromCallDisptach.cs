namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovedDispatchedFromCallDisptach : DbMigration
    {
        public override void Up()
        {
            DropColumn("dbo.CallDispatches", "Dispatched");
        }
        
        public override void Down()
        {
            AddColumn("dbo.CallDispatches", "Dispatched", c => c.Boolean(nullable: false));
        }
    }
}
