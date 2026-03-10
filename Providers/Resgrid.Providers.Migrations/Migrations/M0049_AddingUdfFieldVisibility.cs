using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(49)]
	public class M0049_AddingUdfFieldVisibility : Migration
	{
		public override void Up()
		{
			// Add Visibility column to UdfFields.
			// 0 = Everyone (default, preserves existing behaviour)
			// 1 = DepartmentAndGroupAdmins
			// 2 = DepartmentAdminsOnly
			Alter.Table("UdfFields")
				.AddColumn("Visibility").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{
			Delete.Column("Visibility").FromTable("UdfFields");
		}
	}
}

