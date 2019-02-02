namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedCreatedUpdatedDatesToDepartment : DbMigration
    {
        public override void Up()
        {
            AddColumn("Departments", "CreatedOn", c => c.DateTime());
            AddColumn("Departments", "UpdatedOn", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("Departments", "UpdatedOn");
            DropColumn("Departments", "CreatedOn");
        }
    }
}
