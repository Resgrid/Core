using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Saved incident-map view (center + zoom) on IncidentCommands so the tactical map opens with a
	/// consistent framing for everyone on the incident.
	/// </summary>
	[Migration(97)]
	public class M0097_AddIncidentMapViewPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentcommands").Exists())
				return;

			foreach (var column in new[] { "mapcenterlatitude", "mapcenterlongitude", "mapzoomlevel" })
			{
				if (!Schema.Table("incidentcommands").Column(column).Exists())
					Alter.Table("incidentcommands").AddColumn(column).AsCustom("citext").Nullable();
			}
		}

		public override void Down()
		{
			if (!Schema.Table("incidentcommands").Exists())
				return;

			foreach (var column in new[] { "mapcenterlatitude", "mapcenterlongitude", "mapzoomlevel" })
			{
				if (Schema.Table("incidentcommands").Column(column).Exists())
					Delete.Column(column).FromTable("incidentcommands");
			}
		}
	}
}
