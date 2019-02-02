namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CreateTestDepartment : DbMigration
    {
        public override void Up()
        {
             //Create the System test department
			Sql(Config.DataConfig.TestDepartmentSql);
        }
        
        public override void Down()
        {
        }
    }
}
