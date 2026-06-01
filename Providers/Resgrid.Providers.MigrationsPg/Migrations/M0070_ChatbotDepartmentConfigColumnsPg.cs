using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(70)]
	public class M0070_ChatbotDepartmentConfigColumnsPg : Migration
	{
		public override void Up()
		{
			// Per-department LLM/AI override + rate limits + linking/notification preferences.
			Alter.Table("ChatbotDepartmentConfigs".ToLower())
				.AddColumn("LlmApiEndpoint".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("LlmApiKey".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("LlmModelName".ToLower()).AsCustom("citext").Nullable()
				.AddColumn("MessagesPerUserPerMinute".ToLower()).AsInt32().Nullable()
				.AddColumn("MessagesPerDepartmentPerMinute".ToLower()).AsInt32().Nullable()
				.AddColumn("RequireLinkingConfirmation".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
				.AddColumn("ProactiveNotificationsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("LlmApiEndpoint".ToLower())
				.Column("LlmApiKey".ToLower())
				.Column("LlmModelName".ToLower())
				.Column("MessagesPerUserPerMinute".ToLower())
				.Column("MessagesPerDepartmentPerMinute".ToLower())
				.Column("RequireLinkingConfirmation".ToLower())
				.Column("ProactiveNotificationsEnabled".ToLower())
				.FromTable("ChatbotDepartmentConfigs".ToLower());
		}
	}
}
