using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(70)]
	public class M0070_ChatbotDepartmentConfigColumns : Migration
	{
		public override void Up()
		{
			// Per-department LLM/AI override + rate limits + linking/notification preferences.
			Alter.Table("ChatbotDepartmentConfigs")
				.AddColumn("LlmApiEndpoint").AsString(500).Nullable()
				.AddColumn("LlmApiKey").AsString(1000).Nullable()
				.AddColumn("LlmModelName").AsString(200).Nullable()
				.AddColumn("MessagesPerUserPerMinute").AsInt32().Nullable()
				.AddColumn("MessagesPerDepartmentPerMinute").AsInt32().Nullable()
				.AddColumn("RequireLinkingConfirmation").AsBoolean().NotNullable().WithDefaultValue(true)
				.AddColumn("ProactiveNotificationsEnabled").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			Delete.Column("LlmApiEndpoint")
				.Column("LlmApiKey")
				.Column("LlmModelName")
				.Column("MessagesPerUserPerMinute")
				.Column("MessagesPerDepartmentPerMinute")
				.Column("RequireLinkingConfirmation")
				.Column("ProactiveNotificationsEnabled")
				.FromTable("ChatbotDepartmentConfigs");
		}
	}
}
