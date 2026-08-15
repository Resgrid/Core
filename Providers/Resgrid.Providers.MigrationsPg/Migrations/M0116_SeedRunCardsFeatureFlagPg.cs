using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Seeds the "Dispatch.RunCards" feature flag (off by default) gating the run card dispatch
	/// system (run cards, station-based dispatching, closest unit response, move-up
	/// recommendations); enable globally or via a per-department override to roll out.
	/// </summary>
	[Migration(116)]
	public class M0116_SeedRunCardsFeatureFlagPg : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.DispatchRunCards.
		private const string FlagKey = "Dispatch.RunCards";

		public override void Up()
		{
			// Seeded OFF (isenabledglobally = false). Run cards stay hidden until this flag is
			// enabled globally or via a per-department override. flagtype, isarchived, ispermanent
			// and createdon fall back to their table defaults; the identity PK is omitted so
			// Postgres assigns it.
			// Guarded with WHERE NOT EXISTS so re-running the migration does not violate the unique
			// flagkey index.
			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', " +
				"'Run Card Dispatch', " +
				"'CAD-style run cards with station-based and closest-unit automatic resource selection, multi-alarm escalation and move-up recommendations. Seeded off; enable globally or per-department to roll out.', " +
				"'Dispatch', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });
		}
	}
}
