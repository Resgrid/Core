namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatingDepartmentIdInGroups : DbMigration
    {
        public override void Up()
        {
			Sql(@"	UPDATE t1
					SET t1.DepartmentId = t2.DepartmentId
					FROM dbo.DepartmentGroupMembers AS t1
					INNER JOIN dbo.DepartmentGroups AS t2
					ON t1.DepartmentGroupId = t2.DepartmentGroupId
                ");
		}
        
        public override void Down()
        {
        }
    }
}
