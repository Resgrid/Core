namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System.Data.Entity.Migrations;
    
    public partial class AddedApiKeyToDepartment : DbMigration
    {
        public override void Up()
        {
            AddColumn("Departments", "ApiKey", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("Departments", "ApiKey");
        }
    }
}
