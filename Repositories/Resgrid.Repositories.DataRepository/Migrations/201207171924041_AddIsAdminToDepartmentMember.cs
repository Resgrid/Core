namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddIsAdminToDepartmentMember : DbMigration
    {
        public override void Up()
        {
            AddColumn("DepartmentMembers", "IsAdmin", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("DepartmentMembers", "IsAdmin");
        }
    }
}
