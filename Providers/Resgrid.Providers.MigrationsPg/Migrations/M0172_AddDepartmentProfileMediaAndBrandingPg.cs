using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department Profile elevation (RMS plan section 4.10.1, registry M0172): departmentprofilemedia (logo and
	/// renditions under one per-department mediakey), usedepartmentbrandinginemails on departmentprofiles, and the
	/// one-time copy of a populated legacy logo. PostgreSQL twin of the SQL Server migration. Existence-guarded.
	/// </summary>
	[Migration(172)]
	public class M0172_AddDepartmentProfileMediaAndBrandingPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentprofilemedia").Exists())
			{
				Create.Table("departmentprofilemedia")
					.WithColumn("departmentprofilemediaid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("contenttype").AsCustom("citext").Nullable()
					.WithColumn("width").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("height").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("uploadedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable()
					.WithColumn("mediakey").AsCustom("citext").NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_departmentprofilemedia_department_kind ON departmentprofilemedia (departmentid, kind);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentprofilemedia_mediakey_kind ON departmentprofilemedia (mediakey, kind);");
			}

			if (Schema.Table("departmentprofiles").Exists() && !Schema.Table("departmentprofiles").Column("usedepartmentbrandinginemails").Exists())
			{
				Alter.Table("departmentprofiles").AddColumn("usedepartmentbrandinginemails").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (Schema.Table("departmentprofiles").Exists() && Schema.Table("departmentprofiles").Column("logo").Exists())
			{
				// gen_random_uuid() is built in from PostgreSQL 13; the fleet runs newer.
				Execute.Sql(@"INSERT INTO departmentprofilemedia (departmentprofilemediaid, departmentid, protectionid, kind, contenttype, width, height, bytesize, checksum, data, uploadedbyuserid, uploadedon, mediakey, createdon, modifiedon, rowversion)
SELECT gen_random_uuid()::text, p.departmentid, gen_random_uuid()::text, 1, 'application/octet-stream', 0, 0, octet_length(p.logo), NULL, p.logo, NULL, (now() at time zone 'utc'), replace(gen_random_uuid()::text, '-', ''), (now() at time zone 'utc'), (now() at time zone 'utc'), 1
FROM departmentprofiles p
WHERE p.logo IS NOT NULL AND octet_length(p.logo) > 0
  AND NOT EXISTS (SELECT 1 FROM departmentprofilemedia m WHERE m.departmentid = p.departmentid AND m.kind = 1);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("departmentprofilemedia").Exists())
				Delete.Table("departmentprofilemedia");

			if (Schema.Table("departmentprofiles").Exists() && Schema.Table("departmentprofiles").Column("usedepartmentbrandinginemails").Exists())
				Delete.Column("usedepartmentbrandinginemails").FromTable("departmentprofiles");
		}
	}
}
