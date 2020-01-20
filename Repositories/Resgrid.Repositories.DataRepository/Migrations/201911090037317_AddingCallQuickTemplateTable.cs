namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingCallQuickTemplateTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallQuickTemplates",
                c => new
                    {
                        CallQuickTemplateId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        IsDisabled = c.Boolean(nullable: false),
                        Name = c.String(nullable: false),
                        CallName = c.String(),
                        CallNature = c.String(),
                        CallType = c.String(),
                        CallPriority = c.Int(nullable: false),
                        CreatedByUserId = c.String(maxLength: 128),
                        CreatedOn = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.CallQuickTemplateId)
                .ForeignKey("dbo.AspNetUsers", t => t.CreatedByUserId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId)
                .Index(t => t.CreatedByUserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CallQuickTemplates", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.CallQuickTemplates", "CreatedByUserId", "dbo.AspNetUsers");
            DropIndex("dbo.CallQuickTemplates", new[] { "CreatedByUserId" });
            DropIndex("dbo.CallQuickTemplates", new[] { "DepartmentId" });
            DropTable("dbo.CallQuickTemplates");
        }
    }
}
