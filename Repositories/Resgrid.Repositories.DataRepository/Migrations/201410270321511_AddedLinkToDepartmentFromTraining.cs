namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedLinkToDepartmentFromTraining : DbMigration
    {
        public override void Up()
        {
            CreateIndex("dbo.Trainings", "DepartmentId");
            AddForeignKey("dbo.Trainings", "DepartmentId", "dbo.Departments", "DepartmentId");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Trainings", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.Trainings", new[] { "DepartmentId" });
        }
    }
}
