namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedJobsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Jobs",
                c => new
                    {
                        JobId = c.Int(nullable: false, identity: true),
                        JobType = c.Int(nullable: false),
                        CheckInterval = c.Int(nullable: false),
                        StartTimestamp = c.DateTime(),
                        LastCheckTimestamp = c.DateTime(),
                        DoRestart = c.Boolean(),
                        RestartRequestedTimestamp = c.DateTime(),
                        LastResetTimestamp = c.DateTime(),
                    })
                .PrimaryKey(t => t.JobId);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.Jobs");
        }
    }
}
