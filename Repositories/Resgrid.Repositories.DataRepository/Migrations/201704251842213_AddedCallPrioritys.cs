namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallPrioritys : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentCallPriorities",
                c => new
                    {
                        DepartmentCallPriorityId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Name = c.String(),
                        Color = c.String(),
                        Sort = c.Int(nullable: false),
                        IsDeleted = c.Boolean(nullable: false),
                        IsDefault = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentCallPriorityId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DepartmentCallPriorities", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.DepartmentCallPriorities", new[] { "DepartmentId" });
            DropTable("dbo.DepartmentCallPriorities");
        }
    }
}
