namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedProcessLogsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ProcessLogs",
                c => new
                    {
                        ProcessLogId = c.Int(nullable: false, identity: true),
                        Type = c.Int(nullable: false),
                        SourceId = c.Int(nullable: false),
                        TargetRunTime = c.DateTime(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.ProcessLogId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.ProcessLogs");
        }
    }
}
