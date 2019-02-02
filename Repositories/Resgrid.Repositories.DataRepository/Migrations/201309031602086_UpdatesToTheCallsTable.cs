namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatesToTheCallsTable : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.Users", "Call_CallId", "dbo.Calls");
            DropIndex("dbo.Users", new[] { "Call_CallId" });
            AddForeignKey("dbo.Calls", "ClosedByUserId", "dbo.Users", "UserId");
            CreateIndex("dbo.Calls", "ClosedByUserId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.Calls", new[] { "ClosedByUserId" });
            DropForeignKey("dbo.Calls", "ClosedByUserId", "dbo.Users");
            CreateIndex("dbo.Users", "Call_CallId");
            AddForeignKey("dbo.Users", "Call_CallId", "dbo.Calls", "CallId");
        }
    }
}
