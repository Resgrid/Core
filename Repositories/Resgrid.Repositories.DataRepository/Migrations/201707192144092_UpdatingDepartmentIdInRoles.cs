namespace Resgrid.Repositories.DataRepository.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class UpdatingDepartmentIdInRoles : DbMigration
    {
        public override void Up()
        {
			Sql(@"	UPDATE t1
					SET t1.DepartmentId = t2.DepartmentId
					FROM dbo.PersonnelRoleUsers AS t1
					INNER JOIN dbo.PersonnelRoles AS t2
					ON t1.PersonnelRoleId = t2.PersonnelRoleId
                ");
		}
        
        public override void Down()
        {
        }
    }
}