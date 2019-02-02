namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentIdToFill : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ResourceOrderFills", "FillingDepartmentId", c => c.Int(nullable: false));
            CreateIndex("dbo.ResourceOrderFills", "FillingDepartmentId");
            AddForeignKey("dbo.ResourceOrderFills", "FillingDepartmentId", "dbo.Departments", "DepartmentId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ResourceOrderFills", "FillingDepartmentId", "dbo.Departments");
            DropIndex("dbo.ResourceOrderFills", new[] { "FillingDepartmentId" });
            DropColumn("dbo.ResourceOrderFills", "FillingDepartmentId");
        }
    }
}
