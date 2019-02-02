namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentCallEmailsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentCallEmails",
                c => new
                    {
                        DepartmentCallEmailId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Hostname = c.String(nullable: false, maxLength: 500),
                        Port = c.Int(nullable: false),
                        UseSsl = c.Boolean(nullable: false),
                        Username = c.String(maxLength: 125),
                        Password = c.String(maxLength: 125),
                    })
                .PrimaryKey(t => t.DepartmentCallEmailId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.DepartmentCallEmails", new[] { "DepartmentId" });
            DropForeignKey("dbo.DepartmentCallEmails", "DepartmentId", "dbo.Departments");
            DropTable("dbo.DepartmentCallEmails");
        }
    }
}
