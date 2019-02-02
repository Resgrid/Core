namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentToPersonnelRolesTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.PersonnelRoles", "DepartmentId", c => c.Int(nullable: false));
            AddForeignKey("dbo.PersonnelRoles", "DepartmentId", "dbo.Departments", "DepartmentId", cascadeDelete: true);
            CreateIndex("dbo.PersonnelRoles", "DepartmentId");
        }
        
        public override void Down()
        {
            DropIndex("dbo.PersonnelRoles", new[] { "DepartmentId" });
            DropForeignKey("dbo.PersonnelRoles", "DepartmentId", "dbo.Departments");
            DropColumn("dbo.PersonnelRoles", "DepartmentId");
        }
    }
}
