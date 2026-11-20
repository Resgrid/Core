using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Call start and destination locations plus a pre-defined route. Calls gain the POI the call location was picked from
	/// (LocationPoiId), a free destination for calls whose destination is not a POI (address, "lat,lon" point, what3words) and the
	/// route plan the call follows (RoutePlanId). RouteInstances gain the call a run was started for (CallId) and when the unit reached
	/// the plan's start (StartReachedOn), so a call route first navigates to its start. RecurringCalls gain the same free destination and
	/// route so the calls they create carry them. No foreign keys (a deleted plan or POI leaves the call as it was). The filtered index
	/// serves the call's route runs. Existence-guarded for safe retry.
	/// </summary>
	[Migration(322)]
	public class M0322_AddCallLocationsAndRoutes : Migration
	{
		public override void Up()
		{
			AddColumn("Calls", "LocationPoiId", "int NULL");
			AddColumn("Calls", "DestinationAddress", "nvarchar(500) NULL");
			AddColumn("Calls", "DestinationGeoLocationData", "nvarchar(100) NULL");
			AddColumn("Calls", "DestinationW3W", "nvarchar(100) NULL");
			AddColumn("Calls", "RoutePlanId", "nvarchar(128) NULL");

			AddColumn("RouteInstances", "CallId", "int NULL");
			AddColumn("RouteInstances", "StartReachedOn", "datetime2 NULL");

			AddColumn("RecurringCalls", "DestinationAddress", "nvarchar(500) NULL");
			AddColumn("RecurringCalls", "DestinationGeoLocationData", "nvarchar(100) NULL");
			AddColumn("RecurringCalls", "RoutePlanId", "nvarchar(128) NULL");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RouteInstances_CallId' AND object_id = OBJECT_ID('RouteInstances')) " +
						"CREATE INDEX [IX_RouteInstances_CallId] ON [RouteInstances] ([CallId]) WHERE [CallId] IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RouteInstances_CallId' AND object_id = OBJECT_ID('RouteInstances')) " +
						"DROP INDEX [IX_RouteInstances_CallId] ON [RouteInstances];");

			foreach (var column in new[] { "DestinationAddress", "DestinationGeoLocationData", "RoutePlanId" })
				DropColumn("RecurringCalls", column);

			foreach (var column in new[] { "CallId", "StartReachedOn" })
				DropColumn("RouteInstances", column);

			foreach (var column in new[] { "LocationPoiId", "DestinationAddress", "DestinationGeoLocationData", "DestinationW3W", "RoutePlanId" })
				DropColumn("Calls", column);
		}

		private void AddColumn(string table, string column, string definition)
		{
			Execute.Sql($"IF COL_LENGTH('{table}', '{column}') IS NULL ALTER TABLE [{table}] ADD [{column}] {definition};");
		}

		private void DropColumn(string table, string column)
		{
			Execute.Sql($"IF COL_LENGTH('{table}', '{column}') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [{column}];");
		}
	}
}
