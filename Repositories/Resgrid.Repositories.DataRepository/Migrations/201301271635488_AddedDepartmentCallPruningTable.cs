namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentCallPruningTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DepartmentCallPruning",
                c => new
                    {
                        DepartmentCallPruningId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        PruneUserEnteredCalls = c.Boolean(),
                        UserCallPruneInterval = c.Int(),
                        PruneEmailImportedCalls = c.Boolean(),
                        EmailImportCallPruneInterval = c.Int(),
                    })
                .PrimaryKey(t => t.DepartmentCallPruningId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.DepartmentCallPruning", new[] { "DepartmentId" });
            DropForeignKey("dbo.DepartmentCallPruning", "DepartmentId", "dbo.Departments");
            DropTable("dbo.DepartmentCallPruning");
        }
    }
}
