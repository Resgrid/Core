namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCallTypesTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CallTypes",
                c => new
                    {
                        CallTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.String(nullable: false, maxLength: 100),
                    })
                .PrimaryKey(t => t.CallTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("dbo.CallTypes", new[] { "DepartmentId" });
            DropForeignKey("dbo.CallTypes", "DepartmentId", "dbo.Departments");
            DropTable("dbo.CallTypes");
        }
    }
}
