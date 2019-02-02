namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLogAttachmentsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.LogAttachments",
                c => new
                    {
                        LogAttachmentId = c.Int(nullable: false, identity: true),
                        LogId = c.Int(nullable: false),
                        FileName = c.String(),
                        Data = c.Binary(),
                        UserId = c.Guid(nullable: false),
                        Timestamp = c.DateTime(nullable: false),
                        Size = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.LogAttachmentId)
                .ForeignKey("dbo.Logs", t => t.LogId, cascadeDelete: true)
                .Index(t => t.LogId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.LogAttachments", "LogId", "dbo.Logs");
            DropIndex("dbo.LogAttachments", new[] { "LogId" });
            DropTable("dbo.LogAttachments");
        }
    }
}
