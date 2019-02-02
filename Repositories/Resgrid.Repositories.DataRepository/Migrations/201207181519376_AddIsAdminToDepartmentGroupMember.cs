namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddIsAdminToDepartmentGroupMember : DbMigration
    {
        public override void Up()
        {
            AddColumn("DepartmentGroupMembers", "IsAdmin", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("DepartmentGroupMembers", "IsAdmin");
        }
    }
}
