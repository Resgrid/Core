namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallObject : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "Calls",
                c => new
                    {
                        CallId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        ReportingUserId = c.Guid(nullable: false),
                        Name = c.String(nullable: false, maxLength: 100),
                        NatureOfCall = c.String(nullable: false, maxLength: 500),
                        Notes = c.String(),
                        LoggedOn = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.CallId)
                .ForeignKey("Users", t => t.ReportingUserId, cascadeDelete: true)
                .ForeignKey("Departments", t => t.DepartmentId)
                .Index(t => t.ReportingUserId)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("Calls", new[] { "DepartmentId" });
            DropIndex("Calls", new[] { "ReportingUserId" });
            DropForeignKey("Calls", "DepartmentId", "Departments");
            DropForeignKey("Calls", "ReportingUserId", "Users");
            DropTable("Calls");
        }
    }
}
