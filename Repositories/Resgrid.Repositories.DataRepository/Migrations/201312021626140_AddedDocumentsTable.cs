namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDocumentsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Documents",
                c => new
                    {
                        DocumentId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false),
                        Category = c.String(),
                        Description = c.String(),
                        AdminsOnly = c.Boolean(nullable: false),
                        Data = c.Binary(nullable: false),
                        UserId = c.Guid(nullable: false),
                        AddedOn = c.DateTime(nullable: false),
                        RemoveOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.DocumentId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.DepartmentId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Documents", "UserId", "dbo.Users");
            DropForeignKey("dbo.Documents", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Documents", new[] { "UserId" });
            DropIndex("dbo.Documents", new[] { "DepartmentId" });
            DropTable("dbo.Documents");
        }
    }
}
