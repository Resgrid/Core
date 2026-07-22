using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident-level display name plus the ICP/HQ (command post), Staging, and Rehab locations: a free-form
	/// text description each, and latitude/longitude pairs for Staging and Rehab (the command post already
	/// has coordinate columns from M0077).
	/// </summary>
	[Migration(94)]
	public class M0094_AddIncidentCommandNameAndLocationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentcommands").Exists())
				return;

			foreach (var column in new[]
			{
				"name", "commandpostlocationtext",
				"staginglocationtext", "staginglatitude", "staginglongitude",
				"rehablocationtext", "rehablatitude", "rehablongitude"
			})
			{
				if (!Schema.Table("incidentcommands").Column(column).Exists())
					Alter.Table("incidentcommands").AddColumn(column).AsCustom("citext").Nullable();
			}
		}

		public override void Down()
		{
			if (!Schema.Table("incidentcommands").Exists())
				return;

			foreach (var column in new[]
			{
				"name", "commandpostlocationtext",
				"staginglocationtext", "staginglatitude", "staginglongitude",
				"rehablocationtext", "rehablatitude", "rehablongitude"
			})
			{
				if (Schema.Table("incidentcommands").Column(column).Exists())
					Delete.Column(column).FromTable("incidentcommands");
			}
		}
	}
}
