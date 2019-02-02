namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedPublicApiKeyToDepartmentTable : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "PublicApiKey", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Departments", "PublicApiKey");
        }
    }
}
