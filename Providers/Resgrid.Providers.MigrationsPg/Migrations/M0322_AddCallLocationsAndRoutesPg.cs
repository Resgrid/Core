using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0322_AddCallLocationsAndRoutes: call location POI, free destination and route plan; route runs linked to the
	/// call they were started for and when the unit reached the plan start; recurring calls' free destination and route.
	/// </summary>
	[Migration(322)]
	public class M0322_AddCallLocationsAndRoutesPg : Migration
	{
		public override void Up()
		{
			AddColumn("calls", "locationpoiid", "integer NULL");
			AddColumn("calls", "destinationaddress", "citext NULL");
			AddColumn("calls", "destinationgeolocationdata", "citext NULL");
			AddColumn("calls", "destinationw3w", "citext NULL");
			AddColumn("calls", "routeplanid", "citext NULL");

			AddColumn("routeinstances", "callid", "integer NULL");
			AddColumn("routeinstances", "startreachedon", "timestamp without time zone NULL");

			AddColumn("recurringcalls", "destinationaddress", "citext NULL");
			AddColumn("recurringcalls", "destinationgeolocationdata", "citext NULL");
			AddColumn("recurringcalls", "routeplanid", "citext NULL");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_routeinstances_callid ON routeinstances (callid) WHERE callid IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_routeinstances_callid;");

			foreach (var column in new[] { "destinationaddress", "destinationgeolocationdata", "routeplanid" })
				Execute.Sql($"ALTER TABLE recurringcalls DROP COLUMN IF EXISTS {column};");

			foreach (var column in new[] { "callid", "startreachedon" })
				Execute.Sql($"ALTER TABLE routeinstances DROP COLUMN IF EXISTS {column};");

			foreach (var column in new[] { "locationpoiid", "destinationaddress", "destinationgeolocationdata", "destinationw3w", "routeplanid" })
				Execute.Sql($"ALTER TABLE calls DROP COLUMN IF EXISTS {column};");
		}

		private void AddColumn(string table, string column, string definition)
		{
			Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {column} {definition};");
		}
	}
}
