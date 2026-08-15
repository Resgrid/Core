using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Seeds the "Dispatch.RunCards" feature flag (off by default) gating the run card dispatch
	/// system (run cards, station-based dispatching, closest unit response, move-up
	/// recommendations); enable globally or via a per-department override to roll out.
	/// </summary>
	[Migration(116)]
	public class M0116_SeedRunCardsFeatureFlag : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.DispatchRunCards.
		private const string FlagKey = "Dispatch.RunCards";

		public override void Up()
		{
			// Seeded OFF (IsEnabledGlobally = false). Run cards stay hidden until this flag is
			// enabled globally or via a per-department override. FlagType, IsArchived, IsPermanent
			// and CreatedOn fall back to their table defaults.
			// Guarded with IF NOT EXISTS so re-running the migration does not violate the unique
			// FlagKey index.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', " +
				"'Run Card Dispatch', " +
				"'CAD-style run cards with station-based and closest-unit automatic resource selection, multi-alarm escalation and move-up recommendations. Seeded off; enable globally or per-department to roll out.', " +
				"'Dispatch', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = FlagKey });
		}
	}
}
