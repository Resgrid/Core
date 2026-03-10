using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(49)]
	public class M0049_AddingUdfFieldVisibilityPg : Migration
	{
		public override void Up()
		{
			// Add Visibility column to udffields.
			// 0 = Everyone (default, preserves existing behaviour)
			// 1 = DepartmentAndGroupAdmins
			// 2 = DepartmentAdminsOnly
			Alter.Table("UdfFields".ToLower())
				.AddColumn("Visibility".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{
			Delete.Column("Visibility".ToLower()).FromTable("UdfFields".ToLower());
		}
	}
}

