using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS-1C) product template packs and locked jurisdiction profiles (plan section 4.1, registry M0162): product-scope rows (DepartmentId 0) mirrored from the code catalog so departments see provenance, review dates, locales, units, currency and deprecation; a pack update never mutates a department clone.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys, existence-guarded.
	/// </summary>
	[Migration(162)]
	public class M0162_AddRmsTemplatePacksAndJurisdictionProfilesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmstemplatepackversions").Exists())
			{
				Create.Table("rmstemplatepackversions")
					.WithColumn("rmstemplatepackversionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("packkey").AsCustom("citext").NotNullable()
					.WithColumn("version").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("category").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("ispreview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("definitionkeys").AsCustom("text").Nullable()
					.WithColumn("supportedprofiles").AsCustom("citext").Nullable()
					.WithColumn("supportedlocales").AsCustom("citext").Nullable()
					.WithColumn("releasenotes").AsCustom("text").Nullable()
					.WithColumn("sourceprovenancejson").AsCustom("text").Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("artifactstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("contentchecksum").AsCustom("citext").Nullable()
					.WithColumn("isdeprecated").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("deprecatedbypackkey").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmstemplatepackversions_key_version ON rmstemplatepackversions (departmentid, packkey, version);");
			}

			if (!Schema.Table("rmsjurisdictionprofileversions").Exists())
			{
				Create.Table("rmsjurisdictionprofileversions")
					.WithColumn("rmsjurisdictionprofileversionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("profilekey").AsCustom("citext").NotNullable()
					.WithColumn("version").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("country").AsCustom("citext").Nullable()
					.WithColumn("subdivision").AsCustom("citext").Nullable()
					.WithColumn("agencyscope").AsCustom("citext").Nullable()
					.WithColumn("defaultlocale").AsCustom("citext").Nullable()
					.WithColumn("supportedlocales").AsCustom("citext").Nullable()
					.WithColumn("measurementsystem").AsCustom("citext").Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("defaulttimezone").AsCustom("citext").Nullable()
					.WithColumn("terminologyjson").AsCustom("text").Nullable()
					.WithColumn("standardsjson").AsCustom("text").Nullable()
					.WithColumn("classificationdefault").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("retentionyearsdefault").AsInt32().Nullable()
					.WithColumn("requiredsections").AsCustom("text").Nullable()
					.WithColumn("artifactstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsjurisdictionprofileversions_key_version ON rmsjurisdictionprofileversions (departmentid, profilekey, version);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("rmsjurisdictionprofileversions").Exists())
				Delete.Table("rmsjurisdictionprofileversions");
			if (Schema.Table("rmstemplatepackversions").Exists())
				Delete.Table("rmstemplatepackversions");
		}
	}
}
