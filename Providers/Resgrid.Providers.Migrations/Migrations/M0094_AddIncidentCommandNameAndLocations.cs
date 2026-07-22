using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident-level display name plus the ICP/HQ (command post), Staging, and Rehab locations: a free-form
	/// text description each, and latitude/longitude pairs for Staging and Rehab (the command post already
	/// has coordinate columns from M0077).
	/// </summary>
	[Migration(94)]
	public class M0094_AddIncidentCommandNameAndLocations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentCommands").Exists())
				return;

			if (!Schema.Table("IncidentCommands").Column("Name").Exists())
				Alter.Table("IncidentCommands").AddColumn("Name").AsString(500).Nullable();

			if (!Schema.Table("IncidentCommands").Column("CommandPostLocationText").Exists())
				Alter.Table("IncidentCommands").AddColumn("CommandPostLocationText").AsString(1000).Nullable();

			foreach (var column in new[] { "StagingLocationText", "RehabLocationText" })
			{
				if (!Schema.Table("IncidentCommands").Column(column).Exists())
					Alter.Table("IncidentCommands").AddColumn(column).AsString(1000).Nullable();
			}

			foreach (var column in new[] { "StagingLatitude", "StagingLongitude", "RehabLatitude", "RehabLongitude" })
			{
				if (!Schema.Table("IncidentCommands").Column(column).Exists())
					Alter.Table("IncidentCommands").AddColumn(column).AsString(int.MaxValue).Nullable();
			}
		}

		public override void Down()
		{
			if (!Schema.Table("IncidentCommands").Exists())
				return;

			foreach (var column in new[]
			{
				"Name", "CommandPostLocationText",
				"StagingLocationText", "StagingLatitude", "StagingLongitude",
				"RehabLocationText", "RehabLatitude", "RehabLongitude"
			})
			{
				if (Schema.Table("IncidentCommands").Column(column).Exists())
					Delete.Column(column).FromTable("IncidentCommands");
			}
		}
	}
}
