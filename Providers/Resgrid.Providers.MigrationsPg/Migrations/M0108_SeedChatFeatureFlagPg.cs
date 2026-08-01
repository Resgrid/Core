using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Seeds the "Chat.System" feature flag (off by default) gating the realtime chat system across
	/// web and mobile; enable globally or via a per-department override to roll out.
	/// </summary>
	[Migration(108)]
	public class M0108_SeedChatFeatureFlagPg : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.ChatSystem.
		private const string FlagKey = "Chat.System";

		public override void Up()
		{
			// Seeded OFF (isenabledglobally = false). Chat stays hidden until this flag is enabled
			// globally or via a per-department override. flagtype, isarchived, ispermanent and
			// createdon fall back to their table defaults; the identity PK is omitted so Postgres
			// assigns it.
			// Guarded with WHERE NOT EXISTS so re-running the migration does not violate the unique
			// flagkey index.
			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', " +
				"'Chat System', " +
				"'Realtime chat across web and mobile apps: direct messages, group/department/incident channels, and the chatbot conversation. Seeded off; enable globally or per-department to roll out.', " +
				"'Chat', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });
		}
	}
}
