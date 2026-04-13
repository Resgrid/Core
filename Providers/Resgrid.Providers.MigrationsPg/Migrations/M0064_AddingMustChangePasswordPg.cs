using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(64)]
	public class M0064_AddingMustChangePasswordPg : Migration
	{
		public override void Up()
		{
			Alter.Table("DepartmentMembers".ToLower())
				.AddColumn("MustChangePassword".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("MustChangePassword".ToLower()).FromTable("DepartmentMembers".ToLower());
		}
	}
}
