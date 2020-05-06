namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingDispatchProcotolTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallProtocols",
                c => new
                    {
                        CallProtocolId = c.Int(nullable: false, identity: true),
                        CallId = c.Int(nullable: false),
                        DispatchProtocolId = c.Int(nullable: false),
                        Score = c.Int(nullable: false),
                        Trigger = c.Int(nullable: false),
                        Data = c.String(),
                    })
                .PrimaryKey(t => t.CallProtocolId)
                .ForeignKey("dbo.Calls", t => t.CallId, cascadeDelete: true)
                .ForeignKey("dbo.DispatchProtocols", t => t.DispatchProtocolId, cascadeDelete: true)
                .Index(t => t.CallId)
                .Index(t => t.DispatchProtocolId);
            
            CreateTable(
                "dbo.DispatchProtocols",
                c => new
                    {
                        DispatchProtocolId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(nullable: false, maxLength: 100),
                        Code = c.String(nullable: false, maxLength: 4),
                        IsDisabled = c.Boolean(nullable: false),
                        Description = c.String(maxLength: 500),
                        ProtocolText = c.String(),
                        CreatedOn = c.DateTime(nullable: false),
                        CreatedByUserId = c.String(nullable: false),
                        UpdatedOn = c.DateTime(),
                        MinimumWeight = c.Int(nullable: false),
                        UpdatedByUserId = c.String(nullable: false),
                    })
                .PrimaryKey(t => t.DispatchProtocolId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .Index(t => t.DepartmentId);
            
            CreateTable(
                "dbo.DispatchProtocolAttachments",
                c => new
                    {
                        DispatchProtocolAttachmentId = c.Int(nullable: false, identity: true),
                        DispatchProtocolId = c.Int(nullable: false),
                        FileName = c.String(),
                        FileType = c.String(),
                        Data = c.Binary(),
                    })
                .PrimaryKey(t => t.DispatchProtocolAttachmentId)
                .ForeignKey("dbo.DispatchProtocols", t => t.DispatchProtocolId, cascadeDelete: true)
                .Index(t => t.DispatchProtocolId);
            
            CreateTable(
                "dbo.DispatchProtocolQuestions",
                c => new
                    {
                        DispatchProtocolQuestionId = c.Int(nullable: false, identity: true),
                        DispatchProtocolId = c.Int(nullable: false),
                        Question = c.String(),
                    })
                .PrimaryKey(t => t.DispatchProtocolQuestionId)
                .ForeignKey("dbo.DispatchProtocols", t => t.DispatchProtocolId, cascadeDelete: true)
                .Index(t => t.DispatchProtocolId);
            
            CreateTable(
                "dbo.DispatchProtocolQuestionAnswers",
                c => new
                    {
                        DispatchProtocolQuestionAnswerId = c.Int(nullable: false, identity: true),
                        DispatchProtocolQuestionId = c.Int(nullable: false),
                        Answer = c.String(),
                        Weight = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.DispatchProtocolQuestionAnswerId)
                .ForeignKey("dbo.DispatchProtocolQuestions", t => t.DispatchProtocolQuestionId, cascadeDelete: true)
                .Index(t => t.DispatchProtocolQuestionId);
            
            CreateTable(
                "dbo.DispatchProtocolTriggers",
                c => new
                    {
                        DispatchProtocolTriggerId = c.Int(nullable: false, identity: true),
                        DispatchProtocolId = c.Int(nullable: false),
                        Type = c.Int(nullable: false),
                        StartsOn = c.DateTime(),
                        EndsOn = c.DateTime(),
                        Priority = c.Int(),
                        CallType = c.String(),
                        Geofence = c.String(),
                    })
                .PrimaryKey(t => t.DispatchProtocolTriggerId)
                .ForeignKey("dbo.DispatchProtocols", t => t.DispatchProtocolId, cascadeDelete: true)
                .Index(t => t.DispatchProtocolId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallProtocols", "DispatchProtocolId", "dbo.DispatchProtocols");
            DropForeignKey("dbo.DispatchProtocolTriggers", "DispatchProtocolId", "dbo.DispatchProtocols");
            DropForeignKey("dbo.DispatchProtocolQuestions", "DispatchProtocolId", "dbo.DispatchProtocols");
            DropForeignKey("dbo.DispatchProtocolQuestionAnswers", "DispatchProtocolQuestionId", "dbo.DispatchProtocolQuestions");
            DropForeignKey("dbo.DispatchProtocols", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.DispatchProtocolAttachments", "DispatchProtocolId", "dbo.DispatchProtocols");
            DropForeignKey("dbo.CallProtocols", "CallId", "dbo.Calls");
            DropIndex("dbo.DispatchProtocolTriggers", new[] { "DispatchProtocolId" });
            DropIndex("dbo.DispatchProtocolQuestionAnswers", new[] { "DispatchProtocolQuestionId" });
            DropIndex("dbo.DispatchProtocolQuestions", new[] { "DispatchProtocolId" });
            DropIndex("dbo.DispatchProtocolAttachments", new[] { "DispatchProtocolId" });
            DropIndex("dbo.DispatchProtocols", new[] { "DepartmentId" });
            DropIndex("dbo.CallProtocols", new[] { "DispatchProtocolId" });
            DropIndex("dbo.CallProtocols", new[] { "CallId" });
            DropTable("dbo.DispatchProtocolTriggers");
            DropTable("dbo.DispatchProtocolQuestionAnswers");
            DropTable("dbo.DispatchProtocolQuestions");
            DropTable("dbo.DispatchProtocolAttachments");
            DropTable("dbo.DispatchProtocols");
            DropTable("dbo.CallProtocols");
        }
    }
}
