using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(70)]
	public class M0070_ChatbotDepartmentConfigColumns : Migration
	{
		public override void Up()
		{
			// Per-department LLM/AI override + rate limits + linking/notification preferences.
			// Each column is guarded so the migration is safe on databases where a prior partial
			// apply (or a version renumber) already added some of them.
			const string table = "ChatbotDepartmentConfigs";

			if (!Schema.Table(table).Column("LlmApiEndpoint").Exists())
				Alter.Table(table).AddColumn("LlmApiEndpoint").AsString(500).Nullable();

			if (!Schema.Table(table).Column("LlmApiKey").Exists())
				Alter.Table(table).AddColumn("LlmApiKey").AsString(1000).Nullable();

			if (!Schema.Table(table).Column("LlmModelName").Exists())
				Alter.Table(table).AddColumn("LlmModelName").AsString(200).Nullable();

			if (!Schema.Table(table).Column("MessagesPerUserPerMinute").Exists())
				Alter.Table(table).AddColumn("MessagesPerUserPerMinute").AsInt32().Nullable();

			if (!Schema.Table(table).Column("MessagesPerDepartmentPerMinute").Exists())
				Alter.Table(table).AddColumn("MessagesPerDepartmentPerMinute").AsInt32().Nullable();

			if (!Schema.Table(table).Column("RequireLinkingConfirmation").Exists())
				Alter.Table(table).AddColumn("RequireLinkingConfirmation").AsBoolean().NotNullable().WithDefaultValue(true);

			if (!Schema.Table(table).Column("ProactiveNotificationsEnabled").Exists())
				Alter.Table(table).AddColumn("ProactiveNotificationsEnabled").AsBoolean().NotNullable().WithDefaultValue(false);
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
