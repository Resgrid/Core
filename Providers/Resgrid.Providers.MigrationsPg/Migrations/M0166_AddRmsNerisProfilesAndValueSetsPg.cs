using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0166 (registry M0166, RMS-2): rmsnerisprofiles, rmsnerisvaluesets, rmsneriscrosswalks.</summary>
	[Migration(166)]
	public class M0166_AddRmsNerisProfilesAndValueSetsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsnerisprofiles").Exists())
			{
				Create.Table("rmsnerisprofiles")
					.WithColumn("rmsnerisprofileid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("nerisentityid").AsCustom("citext").Nullable()
					.WithColumn("entityname").AsCustom("citext").Nullable()
					.WithColumn("environment").AsCustom("citext").NotNullable().WithDefaultValue("production")
					.WithColumn("baseurloverride").AsCustom("citext").Nullable()
					.WithColumn("granttype").AsCustom("citext").NotNullable().WithDefaultValue("client_credentials")
					.WithColumn("encryptedcredentialjson").AsString(int.MaxValue).Nullable()
					.WithColumn("contractversion").AsCustom("citext").Nullable()
					.WithColumn("autosubmitonfinalize").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lasttokenissuedon").AsDateTime2().Nullable()
					.WithColumn("lastsuccessfulcallon").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsCustom("citext").Nullable()
					.WithColumn("updatedbyuserid").AsString(128).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsnerisprofiles_department ON rmsnerisprofiles (departmentid);");
			}

			if (!Schema.Table("rmsnerisvaluesets").Exists())
			{
				Create.Table("rmsnerisvaluesets")
					.WithColumn("rmsnerisvaluesetentryid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("contractversion").AsCustom("citext").NotNullable()
					.WithColumn("setkey").AsCustom("citext").NotNullable()
					.WithColumn("code").AsCustom("citext").NotNullable()
					.WithColumn("label").AsCustom("citext").Nullable()
					.WithColumn("parentcode").AsCustom("citext").Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isretired").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsnerisvaluesets_version_set_code ON rmsnerisvaluesets (contractversion, setkey, code);");
			}

			if (!Schema.Table("rmsneriscrosswalks").Exists())
			{
				Create.Table("rmsneriscrosswalks")
					.WithColumn("rmsneriscrosswalkid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("contractversion").AsCustom("citext").NotNullable()
					.WithColumn("setkey").AsCustom("citext").NotNullable()
					.WithColumn("localsource").AsCustom("citext").NotNullable()
					.WithColumn("localcode").AsCustom("citext").NotNullable()
					.WithColumn("neriscode").AsCustom("citext").NotNullable()
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsneriscrosswalks_department_version ON rmsneriscrosswalks (departmentid, contractversion);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsneriscrosswalks_local ON rmsneriscrosswalks (departmentid, contractversion, setkey, localsource, localcode) WHERE deletedon IS NULL;");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsneriscrosswalks", "rmsnerisvaluesets", "rmsnerisprofiles" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
