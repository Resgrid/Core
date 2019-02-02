namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CreatedTrainingSchema : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.TrainingAttachments",
                c => new
                    {
                        TrainingAttachmentId = c.Int(nullable: false, identity: true),
                        TrainingId = c.Int(nullable: false),
                        FileName = c.String(),
                        Data = c.Binary(),
                    })
                .PrimaryKey(t => t.TrainingAttachmentId)
                .ForeignKey("dbo.Trainings", t => t.TrainingId, cascadeDelete: true)
                .Index(t => t.TrainingId);
            
            CreateTable(
                "dbo.Trainings",
                c => new
                    {
                        TrainingId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        CreatedByUserId = c.Guid(nullable: false),
                        Name = c.String(),
                        Description = c.String(),
                        TrainingText = c.String(),
                        MinimumScore = c.Double(nullable: false),
                        CreatedOn = c.DateTime(nullable: false),
                        UsersToAdd = c.String(),
                        GroupsToAdd = c.String(),
                        RolesToAdd = c.String(),
                    })
                .PrimaryKey(t => t.TrainingId);
            
            CreateTable(
                "dbo.TrainingQuestions",
                c => new
                    {
                        TrainingQuestionId = c.Int(nullable: false, identity: true),
                        TrainingId = c.Int(nullable: false),
                        Question = c.String(),
                    })
                .PrimaryKey(t => t.TrainingQuestionId)
                .ForeignKey("dbo.Trainings", t => t.TrainingId, cascadeDelete: true)
                .Index(t => t.TrainingId);
            
            CreateTable(
                "dbo.TrainingQuestionAnswers",
                c => new
                    {
                        TrainingQuestionAnswerId = c.Int(nullable: false, identity: true),
                        TrainingQuestionId = c.Int(nullable: false),
                        Answer = c.String(),
                        Correct = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.TrainingQuestionAnswerId)
                .ForeignKey("dbo.TrainingQuestions", t => t.TrainingQuestionId, cascadeDelete: true)
                .Index(t => t.TrainingQuestionId);
            
            CreateTable(
                "dbo.TrainingUsers",
                c => new
                    {
                        TrainingUserId = c.Int(nullable: false, identity: true),
                        TrainingId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Complete = c.Boolean(nullable: false),
                        Score = c.Double(nullable: false),
                    })
                .PrimaryKey(t => t.TrainingUserId)
                .ForeignKey("dbo.Trainings", t => t.TrainingId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId, cascadeDelete: true)
                .Index(t => t.TrainingId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.TrainingAttachments", "TrainingId", "dbo.Trainings");
            DropForeignKey("dbo.TrainingUsers", "UserId", "dbo.Users");
            DropForeignKey("dbo.TrainingUsers", "TrainingId", "dbo.Trainings");
            DropForeignKey("dbo.TrainingQuestions", "TrainingId", "dbo.Trainings");
            DropForeignKey("dbo.TrainingQuestionAnswers", "TrainingQuestionId", "dbo.TrainingQuestions");
            DropIndex("dbo.TrainingUsers", new[] { "UserId" });
            DropIndex("dbo.TrainingUsers", new[] { "TrainingId" });
            DropIndex("dbo.TrainingQuestionAnswers", new[] { "TrainingQuestionId" });
            DropIndex("dbo.TrainingQuestions", new[] { "TrainingId" });
            DropIndex("dbo.TrainingAttachments", new[] { "TrainingId" });
            DropTable("dbo.TrainingUsers");
            DropTable("dbo.TrainingQuestionAnswers");
            DropTable("dbo.TrainingQuestions");
            DropTable("dbo.Trainings");
            DropTable("dbo.TrainingAttachments");
        }
    }
}
