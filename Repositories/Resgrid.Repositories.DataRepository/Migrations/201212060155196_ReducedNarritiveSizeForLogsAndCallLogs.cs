namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ReducedNarritiveSizeForLogsAndCallLogs : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Logs", "Narrative", c => c.String(nullable: false, maxLength: 4000));
            AlterColumn("dbo.CallLogs", "Narrative", c => c.String(nullable: false, maxLength: 4000));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.CallLogs", "Narrative", c => c.String(nullable: false));
            AlterColumn("dbo.Logs", "Narrative", c => c.String(nullable: false));
        }
    }
}
