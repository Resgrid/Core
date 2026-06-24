using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Offline-first (PostgreSQL): adds a ModifiedOn change cursor to the ad-hoc incident resource tables so they
	/// participate in the /Sync/Changes delta pull. See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Migration(83)]
	public class M0083_AddAdHocResourceChangeTrackingPg : Migration
	{
		private static readonly string[] Tables = { "IncidentAdHocUnits", "IncidentAdHocPersonnel" };

		public override void Up()
		{
			foreach (var table in Tables)
			{
				var t = table.ToLower();
				if (Schema.Table(t).Exists() && !Schema.Table(t).Column("ModifiedOn".ToLower()).Exists())
				{
					Alter.Table(t).AddColumn("ModifiedOn".ToLower()).AsDateTime2().Nullable();
				}
			}
		}

		public override void Down()
		{
			foreach (var table in Tables)
			{
				var t = table.ToLower();
				if (Schema.Table(t).Exists() && Schema.Table(t).Column("ModifiedOn".ToLower()).Exists())
				{
					Delete.Column("ModifiedOn".ToLower()).FromTable(t);
				}
			}
		}
	}
}
