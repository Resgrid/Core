using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// MapLibre mapping, routing and navigation plan (int-Coordination maplibre-mapping-routing-navigation-plan.md):
	/// vehicle size and weight on unit types with per-unit overrides (§10.3, owner Q2), the server-generated route
	/// columns on route plans, stops and instances (§10.3), a contiguous 1-based StopOrder per plan (RD3; relative
	/// order is kept, so readers that sort by StopOrder see the same sequence), and the Navigation.InApp,
	/// Maps.MapLibreWeb and Maps.Offline flags seeded off. Every column is new and nullable or defaulted, so builds that
	/// do not know them keep working (§21). Existence-guarded for safe retry.
	/// </summary>
	[Migration(308)]
	public class M0308_AddMapLibreRoutingFoundation : Migration
	{
		private static readonly string[] VehicleTables = { "UnitTypes", "Units" };

		private static readonly (string Key, string Name, string Description)[] Flags =
		{
			("Navigation.InApp", "Unit In-App Navigation",
				"Turn-by-turn navigation inside the Unit app to calls and route stops, with the unit's live ETA shared to Dispatch and Big Board. Needs a Valhalla or Mapbox routing provider. Seeded off; enable globally or per-department to roll out."),
			("Maps.MapLibreWeb", "MapLibre Web Maps",
				"Renders the web application's maps with MapLibre GL (department vector styles, traffic overlay) instead of Leaflet raster tiles. Seeded off; enable globally or per-department to roll out."),
			("Maps.Offline", "Offline Map Packs",
				"Lets the apps download the department's response area base map and map data for use without a network. Seeded off; enable globally or per-department to roll out.")
		};

		public override void Up()
		{
			foreach (var table in VehicleTables)
			{
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleHeightMeters') IS NULL ALTER TABLE [{table}] ADD [VehicleHeightMeters] float NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleWidthMeters') IS NULL ALTER TABLE [{table}] ADD [VehicleWidthMeters] float NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleLengthMeters') IS NULL ALTER TABLE [{table}] ADD [VehicleLengthMeters] float NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleGrossWeightKg') IS NULL ALTER TABLE [{table}] ADD [VehicleGrossWeightKg] float NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleAxleLoadKg') IS NULL ALTER TABLE [{table}] ADD [VehicleAxleLoadKg] float NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleAxleCount') IS NULL ALTER TABLE [{table}] ADD [VehicleAxleCount] int NULL;");
				Execute.Sql($"IF COL_LENGTH('{table}', 'VehicleHazmat') IS NULL ALTER TABLE [{table}] ADD [VehicleHazmat] bit NULL;");
			}

			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RoutingProfile') IS NULL ALTER TABLE [RoutePlans] ADD [RoutingProfile] int NOT NULL CONSTRAINT [DF_RoutePlans_RoutingProfile] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'AvoidOptions') IS NULL ALTER TABLE [RoutePlans] ADD [AvoidOptions] int NOT NULL CONSTRAINT [DF_RoutePlans_AvoidOptions] DEFAULT(0);");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'StartDepartmentGroupId') IS NULL ALTER TABLE [RoutePlans] ADD [StartDepartmentGroupId] int NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'EndDepartmentGroupId') IS NULL ALTER TABLE [RoutePlans] ADD [EndDepartmentGroupId] int NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteProvider') IS NULL ALTER TABLE [RoutePlans] ADD [RouteProvider] nvarchar(50) NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteComputedOn') IS NULL ALTER TABLE [RoutePlans] ADD [RouteComputedOn] datetime2 NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteInputHash') IS NULL ALTER TABLE [RoutePlans] ADD [RouteInputHash] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteDirectionsJson') IS NULL ALTER TABLE [RoutePlans] ADD [RouteDirectionsJson] nvarchar(max) NULL;");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteIsStale') IS NULL ALTER TABLE [RoutePlans] ADD [RouteIsStale] bit NOT NULL CONSTRAINT [DF_RoutePlans_RouteIsStale] DEFAULT(1);");

			Execute.Sql("IF COL_LENGTH('RouteStops', 'LegDistanceMeters') IS NULL ALTER TABLE [RouteStops] ADD [LegDistanceMeters] float NULL;");
			Execute.Sql("IF COL_LENGTH('RouteStops', 'LegDurationSeconds') IS NULL ALTER TABLE [RouteStops] ADD [LegDurationSeconds] float NULL;");
			Execute.Sql("IF COL_LENGTH('RouteStops', 'PlannedOffsetSeconds') IS NULL ALTER TABLE [RouteStops] ADD [PlannedOffsetSeconds] float NULL;");

			Execute.Sql("IF COL_LENGTH('RouteInstances', 'PlannedDirectionsJson') IS NULL ALTER TABLE [RouteInstances] ADD [PlannedDirectionsJson] nvarchar(max) NULL;");
			Execute.Sql("IF COL_LENGTH('RouteInstances', 'LastRoutedOn') IS NULL ALTER TABLE [RouteInstances] ADD [LastRoutedOn] datetime2 NULL;");

			// The routing profile the plan already had, from the legacy Mapbox profile name.
			Execute.Sql(@"
UPDATE [RoutePlans] SET [RoutingProfile] =
	CASE LOWER(LTRIM(RTRIM(ISNULL([MapboxRouteProfile], ''))))
		WHEN 'driving-traffic' THEN 1
		WHEN 'walking' THEN 3
		WHEN 'cycling' THEN 4
		ELSE 0
	END
WHERE [RoutingProfile] = 0;");

			// Contiguous 1-based stop order per plan (the MVC page wrote 1-based with duplicates after deletes, the API 0-based).
			Execute.Sql(@"
WITH [Ordered] AS (
	SELECT [StopOrder], ROW_NUMBER() OVER (PARTITION BY [RoutePlanId] ORDER BY [StopOrder], [AddedOn], [RouteStopId]) AS [NewOrder]
	FROM [RouteStops]
	WHERE [IsDeleted] = 0
)
UPDATE [Ordered] SET [StopOrder] = [NewOrder] WHERE [StopOrder] <> [NewOrder];");

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + flag.Key + "') " +
					"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
					"VALUES ('" + flag.Key + "', '" + flag.Name + "', '" + flag.Description.Replace("'", "''") + "', 'General', 0);");
			}
		}

		public override void Down()
		{
			foreach (var flag in Flags)
				Delete.FromTable("FeatureFlags").Row(new { FlagKey = flag.Key });

			Execute.Sql("IF COL_LENGTH('RouteInstances', 'LastRoutedOn') IS NOT NULL ALTER TABLE [RouteInstances] DROP COLUMN [LastRoutedOn];");
			Execute.Sql("IF COL_LENGTH('RouteInstances', 'PlannedDirectionsJson') IS NOT NULL ALTER TABLE [RouteInstances] DROP COLUMN [PlannedDirectionsJson];");
			Execute.Sql("IF COL_LENGTH('RouteStops', 'PlannedOffsetSeconds') IS NOT NULL ALTER TABLE [RouteStops] DROP COLUMN [PlannedOffsetSeconds];");
			Execute.Sql("IF COL_LENGTH('RouteStops', 'LegDurationSeconds') IS NOT NULL ALTER TABLE [RouteStops] DROP COLUMN [LegDurationSeconds];");
			Execute.Sql("IF COL_LENGTH('RouteStops', 'LegDistanceMeters') IS NOT NULL ALTER TABLE [RouteStops] DROP COLUMN [LegDistanceMeters];");

			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteIsStale') IS NOT NULL BEGIN ALTER TABLE [RoutePlans] DROP CONSTRAINT [DF_RoutePlans_RouteIsStale]; ALTER TABLE [RoutePlans] DROP COLUMN [RouteIsStale]; END");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteDirectionsJson') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [RouteDirectionsJson];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteInputHash') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [RouteInputHash];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteComputedOn') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [RouteComputedOn];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RouteProvider') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [RouteProvider];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'EndDepartmentGroupId') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [EndDepartmentGroupId];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'StartDepartmentGroupId') IS NOT NULL ALTER TABLE [RoutePlans] DROP COLUMN [StartDepartmentGroupId];");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'AvoidOptions') IS NOT NULL BEGIN ALTER TABLE [RoutePlans] DROP CONSTRAINT [DF_RoutePlans_AvoidOptions]; ALTER TABLE [RoutePlans] DROP COLUMN [AvoidOptions]; END");
			Execute.Sql("IF COL_LENGTH('RoutePlans', 'RoutingProfile') IS NOT NULL BEGIN ALTER TABLE [RoutePlans] DROP CONSTRAINT [DF_RoutePlans_RoutingProfile]; ALTER TABLE [RoutePlans] DROP COLUMN [RoutingProfile]; END");

			foreach (var table in VehicleTables)
			{
				foreach (var column in new[] { "VehicleHazmat", "VehicleAxleCount", "VehicleAxleLoadKg", "VehicleGrossWeightKg", "VehicleLengthMeters", "VehicleWidthMeters", "VehicleHeightMeters" })
					Execute.Sql($"IF COL_LENGTH('{table}', '{column}') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [{column}];");
			}
		}
	}
}
