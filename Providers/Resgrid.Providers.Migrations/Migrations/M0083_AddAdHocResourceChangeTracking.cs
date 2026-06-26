using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Offline-first: adds a ModifiedOn change cursor to the ad-hoc incident resource tables so they participate in
	/// the /Sync/Changes delta pull (previously they were full-refetched). See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(83)]
	public class M0083_AddAdHocResourceChangeTracking : Migration
	{
		private static readonly string[] Tables = { "IncidentAdHocUnits", "IncidentAdHocPersonnel" };

		public override void Up()
		{
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Exists() && !Schema.Table(table).Column("ModifiedOn").Exists())
				{
					Alter.Table(table).AddColumn("ModifiedOn").AsDateTime2().Nullable();
				}
			}
		}

		public override void Down()
		{
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Exists() && Schema.Table(table).Column("ModifiedOn").Exists())
				{
					Delete.Column("ModifiedOn").FromTable(table);
				}
			}
		}
	}
}
