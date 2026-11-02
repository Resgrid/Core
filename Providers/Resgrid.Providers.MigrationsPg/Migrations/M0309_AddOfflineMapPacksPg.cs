using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Offline map packs; see the SQL Server M0309_AddOfflineMapPacks.</summary>
	[Migration(309)]
	public class M0309_AddOfflineMapPacksPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("offlinemappacks").Exists())
			{
				Create.Table("offlinemappacks")
					.WithColumn("offlinemappackid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().Nullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("packkey").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").Nullable()
					.WithColumn("sourceid").AsCustom("citext").Nullable()
					.WithColumn("version").AsCustom("citext").Nullable()
					.WithColumn("bounds").AsCustom("citext").Nullable()
					.WithColumn("minzoom").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxzoom").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sizebytes").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("sha256").AsCustom("citext").Nullable()
					.WithColumn("storagepath").AsCustom("citext").Nullable()
					.WithColumn("filesjson").AsCustom("text").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("builton").AsDateTime2().Nullable()
					.WithColumn("lastrequestedon").AsDateTime2().Nullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("error").AsCustom("text").Nullable();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ix_offlinemappacks_packkey ON offlinemappacks (packkey);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_offlinemappacks_department ON offlinemappacks (departmentid) WHERE departmentid IS NOT NULL;");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_offlinemappacks_status ON offlinemappacks (status, addedon);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("offlinemappacks").Exists())
				Delete.Table("offlinemappacks");
		}
	}
}
