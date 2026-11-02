using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Vehicle size on unit types and units, server-generated route columns, contiguous stop order and the
	/// Navigation.InApp / Maps.MapLibreWeb / Maps.Offline flags; see the SQL Server M0308_AddMapLibreRoutingFoundation.
	/// </summary>
	[Migration(308)]
	public class M0308_AddMapLibreRoutingFoundationPg : Migration
	{
		private static readonly string[] VehicleTables = { "unittypes", "units" };

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
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehicleheightmeters double precision NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehiclewidthmeters double precision NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehiclelengthmeters double precision NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehiclegrossweightkg double precision NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehicleaxleloadkg double precision NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehicleaxlecount integer NULL;");
				Execute.Sql($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS vehiclehazmat boolean NULL;");
			}

			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routingprofile integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS avoidoptions integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS startdepartmentgroupid integer NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS enddepartmentgroupid integer NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routeprovider citext NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routecomputedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routeinputhash citext NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routedirectionsjson text NULL;");
			Execute.Sql("ALTER TABLE routeplans ADD COLUMN IF NOT EXISTS routeisstale boolean NOT NULL DEFAULT true;");

			Execute.Sql("ALTER TABLE routestops ADD COLUMN IF NOT EXISTS legdistancemeters double precision NULL;");
			Execute.Sql("ALTER TABLE routestops ADD COLUMN IF NOT EXISTS legdurationseconds double precision NULL;");
			Execute.Sql("ALTER TABLE routestops ADD COLUMN IF NOT EXISTS plannedoffsetseconds double precision NULL;");

			Execute.Sql("ALTER TABLE routeinstances ADD COLUMN IF NOT EXISTS planneddirectionsjson text NULL;");
			Execute.Sql("ALTER TABLE routeinstances ADD COLUMN IF NOT EXISTS lastroutedon timestamp without time zone NULL;");

			Execute.Sql(@"
UPDATE routeplans SET routingprofile =
	CASE lower(trim(coalesce(mapboxrouteprofile, '')))
		WHEN 'driving-traffic' THEN 1
		WHEN 'walking' THEN 3
		WHEN 'cycling' THEN 4
		ELSE 0
	END
WHERE routingprofile = 0;");

			Execute.Sql(@"
UPDATE routestops s SET stoporder = r.neworder
FROM (
	SELECT routestopid, row_number() OVER (PARTITION BY routeplanid ORDER BY stoporder, addedon, routestopid) AS neworder
	FROM routestops
	WHERE isdeleted = false
) r
WHERE s.routestopid = r.routestopid AND s.stoporder <> r.neworder;");

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
					"SELECT '" + flag.Key + "', '" + flag.Name + "', '" + flag.Description.Replace("'", "''") + "', 'General', false " +
					"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + flag.Key + "');");
			}
		}

		public override void Down()
		{
			foreach (var flag in Flags)
				Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = flag.Key });

			Execute.Sql("ALTER TABLE routeinstances DROP COLUMN IF EXISTS lastroutedon;");
			Execute.Sql("ALTER TABLE routeinstances DROP COLUMN IF EXISTS planneddirectionsjson;");
			Execute.Sql("ALTER TABLE routestops DROP COLUMN IF EXISTS plannedoffsetseconds;");
			Execute.Sql("ALTER TABLE routestops DROP COLUMN IF EXISTS legdurationseconds;");
			Execute.Sql("ALTER TABLE routestops DROP COLUMN IF EXISTS legdistancemeters;");

			foreach (var column in new[] { "routeisstale", "routedirectionsjson", "routeinputhash", "routecomputedon", "routeprovider", "enddepartmentgroupid", "startdepartmentgroupid", "avoidoptions", "routingprofile" })
				Execute.Sql($"ALTER TABLE routeplans DROP COLUMN IF EXISTS {column};");

			foreach (var table in VehicleTables)
			{
				foreach (var column in new[] { "vehiclehazmat", "vehicleaxlecount", "vehicleaxleloadkg", "vehiclegrossweightkg", "vehiclelengthmeters", "vehiclewidthmeters", "vehicleheightmeters" })
					Execute.Sql($"ALTER TABLE {table} DROP COLUMN IF EXISTS {column};");
			}
		}
	}
}
