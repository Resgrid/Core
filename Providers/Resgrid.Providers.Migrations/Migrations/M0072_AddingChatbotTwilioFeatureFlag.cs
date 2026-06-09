using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(72)]
	public class M0072_AddingChatbotTwilioFeatureFlag : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.ChatbotTwilioTextIntegration.
		private const string FlagKey = "Chatbot.TwilioTextIntegration";

		public override void Up()
		{
			// Seeded OFF (IsEnabledGlobally = false). Inbound Twilio SMS keeps the original text-command
			// handling until this flag is enabled globally or via a per-department override. FlagType,
			// IsArchived, IsPermanent and CreatedOn fall back to their table defaults.
			// Guarded with IF NOT EXISTS so re-running the migration does not violate the unique
			// FlagKey index.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + FlagKey + "') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('" + FlagKey + "', " +
				"'Chatbot Twilio Text Integration', " +
				"'When enabled, inbound Twilio SMS is processed by the new chatbot ingress pipeline. When off (globally or for a department) the original text-command handling is used.', " +
				"'Chatbot', 0);");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags").Row(new { FlagKey = FlagKey });
		}
	}
}
