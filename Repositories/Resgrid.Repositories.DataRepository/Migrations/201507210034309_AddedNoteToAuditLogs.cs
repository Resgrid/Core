namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNoteToAuditLogs : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AuditLogs", "Note", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.AuditLogs", "Note");
        }
    }
}
