namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallAttachmentsTableAndLinkToCall : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallAttachments",
                c => new
                    {
                        CallAttachmentId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        CallAttachmentType = c.Int(nullable: false),
                        Data = c.Binary(),
                    })
                .PrimaryKey(t => t.CallAttachmentId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .Index(t => t.CallId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.CallAttachments", new[] { "CallId" });
            DropForeignKey("dbo.CallAttachments", "CallId", "dbo.Calls");
            DropTable("dbo.CallAttachments");
        }
    }
}
