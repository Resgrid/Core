namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingAutomationTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Automations",
                c => new
                    {
                        AutomationId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        AutomationType = c.Int(nullable: false),
                        IsDisabled = c.Boolean(nullable: false),
                        TargetType = c.String(),
                        GroupId = c.Int(),
                        Data = c.String(),
                        CreatedByUserId = c.String(),
                        CreatedOn = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.AutomationId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Automations", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Automations", new[] { "DepartmentId" });
            DropTable("dbo.Automations");
        }
    }
}
