namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedDepartmentTypeToDepartmentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("Departments", "DepartmentType", c => c.String(maxLength: 150));
        }
        
        public override void Down()
        {
            DropColumn("Departments", "DepartmentType");
        }
    }
}
