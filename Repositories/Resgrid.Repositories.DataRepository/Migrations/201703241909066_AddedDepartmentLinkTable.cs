namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentLinkTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentLinks",
                c => new
                    {
                        DepartmentLinkId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        DepartmentColor = c.String(),
                        DepartmentShareCalls = c.Boolean(nullable: false),
                        DepartmentShareUnits = c.Boolean(nullable: false),
                        DepartmentSharePersonnel = c.Boolean(nullable: false),
                        LinkedDepartmentId = c.Int(nullable: false),
                        LinkEnabled = c.Boolean(nullable: false),
                        LinkedDepartmentColor = c.String(),
                        LinkedDepartmentShareCalls = c.Boolean(nullable: false),
                        LinkedDepartmentShareUnits = c.Boolean(nullable: false),
                        LinkedDepartmentSharePersonnel = c.Boolean(nullable: false),
                        LinkCreated = c.DateTime(nullable: false),
                        LinkAccepted = c.DateTime(),
                    })
                .PrimaryKey(t => t.DepartmentLinkId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId)
                .ForeignKey("dbo.Departments", t => t.LinkedDepartmentId)
                .Index(t => t.DepartmentId)
                .Index(t => t.LinkedDepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentLinks", "LinkedDepartmentId", "dbo.Departments");
            DropForeignKey("dbo.DepartmentLinks", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.DepartmentLinks", new[] { "LinkedDepartmentId" });
            DropIndex("dbo.DepartmentLinks", new[] { "DepartmentId" });
            DropTable("dbo.DepartmentLinks");
        }
    }
}
