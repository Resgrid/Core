namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDocumentFilesTableAndUnitIdToPushTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentFiles",
                c => new
                    {
                        DepartmentFileId = c.Guid(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.Int(nullable: false),
                        FileName = c.String(nullable: false),
                        Data = c.Binary(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentFileId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
            AddColumn("dbo.PushUris", "UnitId", c => c.Int());
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentFiles", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.DepartmentFiles", new[] { "DepartmentId" });
            DropColumn("dbo.PushUris", "UnitId");
            DropTable("dbo.DepartmentFiles");
        }
    }
}
