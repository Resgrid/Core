using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Saved incident-map view (center + zoom) on IncidentCommands so the tactical map opens with a
	/// consistent framing for everyone on the incident.
	/// </summary>
	[Migration(97)]
	public class M0097_AddIncidentMapView : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentCommands").Exists())
				return;

			foreach (var column in new[] { "MapCenterLatitude", "MapCenterLongitude", "MapZoomLevel" })
			{
				if (!Schema.Table("IncidentCommands").Column(column).Exists())
					Alter.Table("IncidentCommands").AddColumn(column).AsString(int.MaxValue).Nullable();
			}
		}

		public override void Down()
		{
			if (!Schema.Table("IncidentCommands").Exists())
				return;

			foreach (var column in new[] { "MapCenterLatitude", "MapCenterLongitude", "MapZoomLevel" })
			{
				if (Schema.Table("IncidentCommands").Column(column).Exists())
					Delete.Column(column).FromTable("IncidentCommands");
			}
		}
	}
}
