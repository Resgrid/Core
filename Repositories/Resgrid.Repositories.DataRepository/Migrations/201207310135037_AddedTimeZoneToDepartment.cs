namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedTimeZoneToDepartment : DbMigration
    {
        public override void Up()
        {
            AddColumn("Departments", "TimeZone", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("Departments", "TimeZone");
        }
    }
}
