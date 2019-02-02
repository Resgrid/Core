namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallToLogTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Logs", "CallId", c => c.Int());
            CreateIndex("dbo.Logs", "CallId");
            AddForeignKey("dbo.Logs", "CallId", "dbo.Calls", "CallId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Logs", "CallId", "dbo.Calls");
            DropIndex("dbo.Logs", new[] { "CallId" });
            DropColumn("dbo.Logs", "CallId");
        }
    }
}
