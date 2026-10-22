using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Custom unit, call and POI type icons: typeicons (the uploaded, re-encoded PNG per type under an opaque iconkey;
	/// typekind 1 unit, 2 call, 3 POI) and iconkey on unittypes, calltypes and poitypes mirroring the current key.
	/// PostgreSQL twin of the SQL Server migration. iconkey is varchar, not citext, so the anonymous endpoint's string
	/// parameter compares exactly and uses the unique index.
	/// </summary>
	[Migration(285)]
	public class M0285_AddTypeIconsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("typeicons").Exists())
			{
				Create.Table("typeicons")
					.WithColumn("typeiconid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("typekind").AsInt32().NotNullable()
					.WithColumn("typeid").AsInt32().NotNullable()
					.WithColumn("iconkey").AsString(64).NotNullable()
					.WithColumn("contenttype").AsCustom("citext").Nullable()
					.WithColumn("width").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("height").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("uploadedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_typeicons_iconkey ON typeicons (iconkey);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_typeicons_department_kind_type ON typeicons (departmentid, typekind, typeid);");
			}

			Execute.Sql("ALTER TABLE unittypes ADD COLUMN IF NOT EXISTS iconkey varchar(64) NULL;");
			Execute.Sql("ALTER TABLE calltypes ADD COLUMN IF NOT EXISTS iconkey varchar(64) NULL;");
			Execute.Sql("ALTER TABLE poitypes ADD COLUMN IF NOT EXISTS iconkey varchar(64) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE poitypes DROP COLUMN IF EXISTS iconkey;");
			Execute.Sql("ALTER TABLE calltypes DROP COLUMN IF EXISTS iconkey;");
			Execute.Sql("ALTER TABLE unittypes DROP COLUMN IF EXISTS iconkey;");

			if (Schema.Table("typeicons").Exists())
				Delete.Table("typeicons");
		}
	}
}
