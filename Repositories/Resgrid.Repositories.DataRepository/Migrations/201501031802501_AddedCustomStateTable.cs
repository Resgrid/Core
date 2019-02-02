namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedCustomStateTable : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CustomStates",
                c => new
                    {
                        CustomStateTypeId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        Type = c.Int(nullable: false),
                        Reference = c.String(),
                        Name = c.String(nullable: false),
                        Description = c.String(),
                        ButtonText = c.String(nullable: false),
                        ButtonColor = c.String(nullable: false),
                        GpsRequired = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.CustomStateTypeId)
                .ForeignKey("dbo.Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.CustomStates", "DepartmentId", "dbo.Departments");
            DropIndex("dbo.CustomStates", new[] { "DepartmentId" });
            DropTable("dbo.CustomStates");
        }
    }
}
