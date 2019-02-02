namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedSecretToDepartment : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Departments", "SharedSecret", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Departments", "SharedSecret");
        }
    }
}
