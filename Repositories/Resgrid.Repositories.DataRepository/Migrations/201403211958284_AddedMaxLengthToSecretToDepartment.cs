namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedMaxLengthToSecretToDepartment : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.Departments", "SharedSecret", c => c.String(maxLength: 20));
        }
        
        public override void Down()
        {
            AlterColumn("dbo.Departments", "SharedSecret", c => c.String());
        }
    }
}
