namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatedAuditLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AuditLogs", "Message", c => c.String());
            AddColumn("dbo.AuditLogs", "LoggedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.AuditLogs", "LoggedOn");
            DropColumn("dbo.AuditLogs", "Message");
        }
    }
}
