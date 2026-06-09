using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(72)]
	public class M0072_AddingChatbotTwilioFeatureFlagPg : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.ChatbotTwilioTextIntegration.
		private const string FlagKey = "Chatbot.TwilioTextIntegration";

		public override void Up()
		{
			// Seeded OFF (isenabledglobally = false). Inbound Twilio SMS keeps the original text-command
			// handling until this flag is enabled globally or via a per-department override. flagtype,
			// isarchived, ispermanent and createdon fall back to their table defaults; the identity PK is
			// omitted so Postgres assigns it.
			// Guarded with WHERE NOT EXISTS so re-running the migration does not violate the unique
			// flagkey index.
			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', " +
				"'Chatbot Twilio Text Integration', " +
				"'When enabled, inbound Twilio SMS is processed by the new chatbot ingress pipeline. When off (globally or for a department) the original text-command handling is used.', " +
				"'Chatbot', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });
		}
	}
}
