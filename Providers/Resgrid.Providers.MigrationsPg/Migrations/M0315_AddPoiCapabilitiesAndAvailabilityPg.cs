using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Destination capabilities and availability; see the SQL Server M0315_AddPoiCapabilitiesAndAvailability.</summary>
	[Migration(315)]
	public class M0315_AddPoiCapabilitiesAndAvailabilityPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("poicapabilities").Exists())
			{
				Create.Table("poicapabilities")
					.WithColumn("poicapabilityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("sort").AsInt32().NotNullable().WithDefaultValue(0);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_poicapabilities_department ON poicapabilities (departmentid);");
			}

			if (!Schema.Table("poicapabilityassignments").Exists())
			{
				Create.Table("poicapabilityassignments")
					.WithColumn("poicapabilityassignmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("poiid").AsInt32().NotNullable()
					.WithColumn("poicapabilityid").AsCustom("citext").NotNullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_poicapabilityassignments_department ON poicapabilityassignments (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_poicapabilityassignments_poi_capability ON poicapabilityassignments (poiid, poicapabilityid);");
			}

			if (!Schema.Table("poiavailabilities").Exists())
			{
				Create.Table("poiavailabilities")
					.WithColumn("poiavailabilityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("poiid").AsInt32().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unavailablecapabilityids").AsCustom("citext").Nullable()
					.WithColumn("note").AsCustom("citext").Nullable()
					.WithColumn("setbyuserid").AsCustom("citext").Nullable()
					.WithColumn("seton").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_poiavailabilities_department ON poiavailabilities (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_poiavailabilities_poi ON poiavailabilities (poiid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("poiavailabilities").Exists())
				Delete.Table("poiavailabilities");

			if (Schema.Table("poicapabilityassignments").Exists())
				Delete.Table("poicapabilityassignments");

			if (Schema.Table("poicapabilities").Exists())
				Delete.Table("poicapabilities");
		}
	}
}
