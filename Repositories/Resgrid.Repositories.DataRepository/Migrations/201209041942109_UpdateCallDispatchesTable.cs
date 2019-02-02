namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdateCallDispatchesTable : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.CallDispatches", "ActionLogId", c => c.Int());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.CallDispatches", "ActionLogId", c => c.Int(nullable: false));
        }
    }
}
