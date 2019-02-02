namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLoggedOnToAuditLogs : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AuditLogs", "LoggedOn", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.AuditLogs", "LoggedOn");
        }
    }
}
