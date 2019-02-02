namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCertificationTables : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentCertificationTypes",
                c => new
                    {
                        DepartmentCertificationTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.String(nullable: false, maxLength: 100),
                    })
                .PrimaryKey(t => t.DepartmentCertificationTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
            CreateTable(
                "dbo.PersonnelCertifications",
                c => new
                    {
                        PersonnelCertificationId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Name = c.String(nullable: false),
                        Number = c.String(),
                        Type = c.String(),
                        Area = c.String(),
                        ExpiresOn = c.DateTime(),
                        RecievedOn = c.DateTime(),
                    })
                .PrimaryKey(t => t.PersonnelCertificationId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserId)
                .Index(t => t.DepartmentId)
                .Index(t => t.UserId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.PersonnelCertifications", "UserId", "dbo.Users");
            DropForeignKey("dbo.PersonnelCertifications", "DepartmentId", "dbo.Departments");
            DropForeignKey("dbo.DepartmentCertificationTypes", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.PersonnelCertifications", new[] { "UserId" });
            DropIndex("dbo.PersonnelCertifications", new[] { "DepartmentId" });
            DropIndex("dbo.DepartmentCertificationTypes", new[] { "DepartmentId" });
            DropTable("dbo.PersonnelCertifications");
            DropTable("dbo.DepartmentCertificationTypes");
        }
    }
}
