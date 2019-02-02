namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class AddedDefaultAndActiveToDepartmentMembersTable : DbMigration
	{
		public override void Up()
		{
			AddColumn("dbo.DepartmentMembers", "IsDefault", c => c.Boolean(nullable: false));
			AddColumn("dbo.DepartmentMembers", "IsActive", c => c.Boolean(nullable: false));

			Sql(@"
							UPDATE [dbo].[DepartmentMembers] SET IsDefault = 1, IsActive = 1
						");
		}

		public override void Down()
		{
			DropColumn("dbo.DepartmentMembers", "IsActive");
			DropColumn("dbo.DepartmentMembers", "IsDefault");
		}
	}
}
