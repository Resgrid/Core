namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class ChangedDepartmentGroupNameToString : DbMigration
    {
        public override void Up()
        {
            AlterColumn("DepartmentGroups", "Name", c => c.String(nullable: false, maxLength: 50));
        }
        
        public override void Down()
        {
            AlterColumn("DepartmentGroups", "Name", c => c.Int(nullable: false));
        }
    }
}
