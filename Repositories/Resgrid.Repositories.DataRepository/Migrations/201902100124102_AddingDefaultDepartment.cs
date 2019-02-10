namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddingDefaultDepartment : DbMigration
    {
        public override void Up()
        {
			Sql(Config.DataConfig.CreatingTheDefaultDepartmentSql);
		}
        
        public override void Down()
        {
        }
    }
}
