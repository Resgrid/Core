using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Travel modes and road closures; see the SQL Server M0314_AddTravelModesAndRoadClosures.</summary>
	[Migration(314)]
	public class M0314_AddTravelModesAndRoadClosuresPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE unittypes ADD COLUMN IF NOT EXISTS travelmode integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE unittypes ADD COLUMN IF NOT EXISTS travelspeedkmh double precision NULL;");
			Execute.Sql("ALTER TABLE units ADD COLUMN IF NOT EXISTS travelmode integer NULL;");
			Execute.Sql("ALTER TABLE units ADD COLUMN IF NOT EXISTS travelspeedkmh double precision NULL;");

			if (!Schema.Table("roadclosures").Exists())
			{
				Create.Table("roadclosures")
					.WithColumn("roadclosureid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("closuretype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("severity").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("speedpercent").AsInt32().NotNullable().WithDefaultValue(50)
					.WithColumn("detourminutes").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("appliesto").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("geometryjson").AsCustom("text").Nullable()
					.WithColumn("windowsjson").AsCustom("text").Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("closedby").AsCustom("citext").Nullable()
					.WithColumn("authority").AsCustom("citext").Nullable()
					.WithColumn("contactname").AsCustom("citext").Nullable()
					.WithColumn("contactdetails").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_roadclosures_department ON roadclosures (departmentid, createdon);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("roadclosures").Exists())
				Delete.Table("roadclosures");

			Execute.Sql("ALTER TABLE units DROP COLUMN IF EXISTS travelspeedkmh;");
			Execute.Sql("ALTER TABLE units DROP COLUMN IF EXISTS travelmode;");
			Execute.Sql("ALTER TABLE unittypes DROP COLUMN IF EXISTS travelspeedkmh;");
			Execute.Sql("ALTER TABLE unittypes DROP COLUMN IF EXISTS travelmode;");
		}
	}
}
