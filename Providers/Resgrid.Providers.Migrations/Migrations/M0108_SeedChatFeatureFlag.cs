using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Seeds the "Chat.System" feature flag (off by default) gating the realtime chat system across
	/// web and mobile; enable globally or via a per-department override to roll out.
	/// </summary>
	[Migration(108)]
	public class M0108_SeedChatFeatureFlag : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.ChatSystem.
		private const string FlagKey = "Chat.System";

		public override void Up()
		{
			// Seeded OFF (IsEnabledGlobally = false). Chat stays hidden until this flag is enabled
			// globally or via a per-department override. FlagType, IsArchived, IsPermanent and
			// CreatedOn fall back to their table defaults.
			// Guarded with IF NOT EXISTS so re-running the migration does not violate the unique
			// FlagKey index.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', " +
				"'Chat System', " +
				"'Realtime chat across web and mobile apps: direct messages, group/department/incident channels, and the chatbot conversation. Seeded off; enable globally or per-department to roll out.', " +
				"'Chat', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = FlagKey });
		}
	}
}
