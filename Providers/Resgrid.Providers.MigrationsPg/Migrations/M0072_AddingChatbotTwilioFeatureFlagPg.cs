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
			Insert.IntoTable("FeatureFlags".ToLower()).Row(new
			{
				flagkey = FlagKey,
				name = "Chatbot Twilio Text Integration",
				description = "When enabled, inbound Twilio SMS is processed by the new chatbot ingress pipeline. When off (globally or for a department) the original text-command handling is used.",
				category = "Chatbot",
				isenabledglobally = false
			});
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });
		}
	}
}
