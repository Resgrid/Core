namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemovedFilledDepartmentFromFills : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.ResourceOrderFills", "FillingDepartmentId", "dbo.Departments");
            DropIndex("dbo.ResourceOrderFills", new[] { "FillingDepartmentId" });
            DropColumn("dbo.ResourceOrderFills", "FillingDepartmentId");
        }
        
        public override void Down()
        {
            AddColumn("dbo.ResourceOrderFills", "FillingDepartmentId", c => c.Int(nullable: false));
            CreateIndex("dbo.ResourceOrderFills", "FillingDepartmentId");
            AddForeignKey("dbo.ResourceOrderFills", "FillingDepartmentId", "dbo.Departments", "DepartmentId");
        }
    }
}
