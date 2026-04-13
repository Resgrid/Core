using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(64)]
	public class M0064_AddingMustChangePassword : Migration
	{
		public override void Up()
		{
			Alter.Table("DepartmentMembers")
				.AddColumn("MustChangePassword").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("MustChangePassword").FromTable("DepartmentMembers");
		}
	}
}
