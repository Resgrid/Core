using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Background POI imports; see the SQL Server M0313_AddPoiImports.</summary>
	[Migration(313)]
	public class M0313_AddPoiImportsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("poiimports").Exists())
			{
				Create.Table("poiimports")
					.WithColumn("poiimportid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("poitypeid").AsInt32().NotNullable()
					.WithColumn("poitypename").AsCustom("citext").Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("filename").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("totalrows").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("skippedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("importedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("geocodedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unlocatedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lookupfailedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lookupnotconfigured").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("nottriedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("geocodinglimitreached").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("pendingretrycount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("retryafter").AsDateTime2().Nullable()
					.WithColumn("rowsjson").AsCustom("text").Nullable()
					.WithColumn("unlocatedjson").AsCustom("text").Nullable()
					.WithColumn("error").AsCustom("text").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("heartbeaton").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("attempts").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_poiimports_status ON poiimports (status, addedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_poiimports_department ON poiimports (departmentid, addedon);");
			}

			if (!Schema.Table("pois").Column("poiimportid").Exists())
			{
				Alter.Table("pois").AddColumn("poiimportid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_pois_poiimportid ON pois (poiimportid) WHERE poiimportid IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("pois").Column("poiimportid").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ix_pois_poiimportid;");
				Delete.Column("poiimportid").FromTable("pois");
			}

			if (Schema.Table("poiimports").Exists())
				Delete.Table("poiimports");
		}
	}
}
