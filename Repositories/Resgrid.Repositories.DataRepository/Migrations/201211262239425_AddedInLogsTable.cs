namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedInLogsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Logs",
                c => new
                    {
                        LogId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Narrative = c.String(nullable: false),
                        StartedOn = c.DateTime(),
                        EndedOn = c.DateTime(),
                        LoggedOn = c.DateTime(nullable: false),
                        LoggedByUserId = c.Guid(nullable: false),
                    })
                .PrimaryKey(t => t.LogId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.Users", t => t.LoggedByUserId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.LoggedByUserId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.Logs", new[] { "LoggedByUserId" });
            DropIndex("dbo.Logs", new[] { "DepartmentId" });
            DropForeignKey("dbo.Logs", "LoggedByUserId", "dbo.Users");
            DropForeignKey("dbo.Logs", "DepartmentId", "dbo.Departments");
            DropTable("dbo.Logs");
        }
    }
}
