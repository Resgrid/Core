namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedNotesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Notes",
                c => new
                    {
                        NoteId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Title = c.String(nullable: false),
                        Body = c.String(nullable: false),
                        IsAdminOnly = c.Boolean(nullable: false),
                        Color = c.String(),
                        Categories = c.String(),
                        ExpiresOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.NoteId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Notes", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Notes", new[] { "DepartmentId" });
            DropTable("dbo.Notes");
        }
    }
}
