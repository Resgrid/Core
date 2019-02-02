namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddedStaffingScheduleChangesToTestDepartment : DbMigration
    {
        public override void Up()
        {
	        Sql(Config.DataConfig.TestDepartmentDataSql);
        }
        
        public override void Down()
        {
        }
    }
}
