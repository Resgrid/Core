namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedFileTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Files",
                c => new
                    {
                        FileId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        MessageId = c.Int(),
                        FileName = c.String(),
                        FileType = c.String(),
                        Data = c.Binary(),
                        Timestamp = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.FileId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.Messages", t => t.MessageId)
                .Index(t => t.DepartmentId)
                .Index(t => t.MessageId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Files", "MessageId", "dbo.Messages");
            DropForeignKey("dbo.Files", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Files", new[] { "MessageId" });
            DropIndex("dbo.Files", new[] { "DepartmentId" });
            DropTable("dbo.Files");
        }
    }
}
