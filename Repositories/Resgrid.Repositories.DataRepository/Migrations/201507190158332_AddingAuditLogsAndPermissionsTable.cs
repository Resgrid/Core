namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingAuditLogsAndPermissionsTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.AuditLogs",
                c => new
                    {
                        AuditLogId = c.Int(nullable: false, identity: true),
                        LogType = c.Int(nullable: false),
                        DepartmentId = c.Int(nullable: false),
                        UserId = c.Guid(nullable: false),
                        Data = c.String(),
                    })
                .PrimaryKey(t => t.AuditLogId);
            
            CreateTable(
                "dbo.Permissions",
                c => new
                    {
                        PermissionId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        PermissionType = c.Int(nullable: false),
                        Action = c.Int(nullable: false),
                        Data = c.String(),
                    })
                .PrimaryKey(t => t.PermissionId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Permissions", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Permissions", new[] { "DepartmentId" });
            DropTable("dbo.Permissions");
            DropTable("dbo.AuditLogs");
        }
    }
}
