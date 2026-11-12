using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Travel modes and road closures for dispatch ranking. Unit types get how their units travel (UnitTravelModes: 0
	/// driving, 1 cycling, 2 walking, 3 air; default 0, so every existing type keeps driving) and an optional average speed in
	/// km/h; units may override both (null = the type's). New table RoadClosures: drawn closed or slowed roads and areas
	/// (GeoJSON), what closed them, who, why and who to contact, full or partial, the travel modes they stop and the
	/// periods they are in effect (JSON windows in UTC). No foreign keys: the department purge removes the rows
	/// (UnlinkedDepartmentCleanup). Every column is new and nullable or defaulted, so builds that do not know them keep
	/// working. Existence-guarded for safe retry.
	/// </summary>
	[Migration(314)]
	public class M0314_AddTravelModesAndRoadClosures : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'TravelMode') IS NULL ALTER TABLE [UnitTypes] ADD [TravelMode] int NOT NULL CONSTRAINT [DF_UnitTypes_TravelMode] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'TravelSpeedKmh') IS NULL ALTER TABLE [UnitTypes] ADD [TravelSpeedKmh] float NULL;");
			Execute.Sql("IF COL_LENGTH('Units', 'TravelMode') IS NULL ALTER TABLE [Units] ADD [TravelMode] int NULL;");
			Execute.Sql("IF COL_LENGTH('Units', 'TravelSpeedKmh') IS NULL ALTER TABLE [Units] ADD [TravelSpeedKmh] float NULL;");

			if (!Schema.Table("RoadClosures").Exists())
			{
				Create.Table("RoadClosures")
					.WithColumn("RoadClosureId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("ClosureType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Severity").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SpeedPercent").AsInt32().NotNullable().WithDefaultValue(50)
					.WithColumn("DetourMinutes").AsInt32().NotNullable().WithDefaultValue(5)
					.WithColumn("AppliesTo").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("GeometryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("WindowsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("ClosedBy").AsString(200).Nullable()
					.WithColumn("Authority").AsString(200).Nullable()
					.WithColumn("ContactName").AsString(200).Nullable()
					.WithColumn("ContactDetails").AsString(500).Nullable()
					.WithColumn("Notes").AsString(4000).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RoadClosures_Department ON RoadClosures (DepartmentId, CreatedOn);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("RoadClosures").Exists())
				Delete.Table("RoadClosures");

			Execute.Sql("IF COL_LENGTH('Units', 'TravelSpeedKmh') IS NOT NULL ALTER TABLE [Units] DROP COLUMN [TravelSpeedKmh];");
			Execute.Sql("IF COL_LENGTH('Units', 'TravelMode') IS NOT NULL ALTER TABLE [Units] DROP COLUMN [TravelMode];");
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'TravelSpeedKmh') IS NOT NULL ALTER TABLE [UnitTypes] DROP COLUMN [TravelSpeedKmh];");
			Execute.Sql("IF COL_LENGTH('UnitTypes', 'TravelMode') IS NOT NULL BEGIN ALTER TABLE [UnitTypes] DROP CONSTRAINT [DF_UnitTypes_TravelMode]; ALTER TABLE [UnitTypes] DROP COLUMN [TravelMode]; END");
		}
	}
}
