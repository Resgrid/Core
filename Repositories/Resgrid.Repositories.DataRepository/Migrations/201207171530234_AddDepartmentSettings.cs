namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddDepartmentSettings : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "DepartmentSettings",
                c => new
                    {
                        DepartmentSettingId = c.Int(nullable: false, identity: true),
                        DepartmentId = c.Int(nullable: false),
                        SettingType = c.Int(nullable: false),
                        Setting = c.String(nullable: false),
                    })
                .PrimaryKey(t => t.DepartmentSettingId)
                .ForeignKey("Departments", t => t.DepartmentId, cascadeDelete: true)
                .Index(t => t.DepartmentId);
            
        }
        
        public override void Down()
        {
            DropIndex("DepartmentSettings", new[] { "DepartmentId" });
            DropForeignKey("DepartmentSettings", "DepartmentId", "Departments");
            DropTable("DepartmentSettings");
        }
    }
}
