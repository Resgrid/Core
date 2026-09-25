using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0241: per-department AI dispatch settings (departmentaidispatchconfigs). Same number, lower-case identifiers.</summary>
	[Migration(241)]
	public class M0241_AddAiDispatchSettingsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("departmentaidispatchconfigs").Exists())
				return;

			Create.Table("departmentaidispatchconfigs")
				.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
				.WithColumn("minimumconfidence").AsDecimal(3, 2).Nullable()
				.WithColumn("senderallowlist").AsString(4000).Nullable()
				.WithColumn("monthlytokencap").AsInt32().Nullable()
				.WithColumn("auditretentiondays").AsInt32().NotNullable().WithDefaultValue(365)
				.WithColumn("fillcalltype").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("filladdress").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("fillcontact").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("fillincidentnumber").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("renameplaceholder").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("addsummarynote").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("flagrelatedcalls").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("revision").AsInt64().NotNullable()
				.WithColumn("updatedbyuserid").AsString(128).Nullable()
				.WithColumn("updatedonutc").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS departmentaidispatchconfigs;");
		}
	}
}
