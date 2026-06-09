using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(70)]
	public class M0070_ChatbotDepartmentConfigColumnsPg : Migration
	{
		public override void Up()
		{
			// Per-department LLM/AI override + rate limits + linking/notification preferences.
			// Each column is guarded so the migration is safe on databases where a prior partial
			// apply (or a version renumber) already added some of them.
			var table = "ChatbotDepartmentConfigs".ToLower();

			if (!Schema.Table(table).Column("LlmApiEndpoint".ToLower()).Exists())
				Alter.Table(table).AddColumn("LlmApiEndpoint".ToLower()).AsCustom("citext").Nullable();

			if (!Schema.Table(table).Column("LlmApiKey".ToLower()).Exists())
				Alter.Table(table).AddColumn("LlmApiKey".ToLower()).AsCustom("citext").Nullable();

			if (!Schema.Table(table).Column("LlmModelName".ToLower()).Exists())
				Alter.Table(table).AddColumn("LlmModelName".ToLower()).AsCustom("citext").Nullable();

			if (!Schema.Table(table).Column("MessagesPerUserPerMinute".ToLower()).Exists())
				Alter.Table(table).AddColumn("MessagesPerUserPerMinute".ToLower()).AsInt32().Nullable();

			if (!Schema.Table(table).Column("MessagesPerDepartmentPerMinute".ToLower()).Exists())
				Alter.Table(table).AddColumn("MessagesPerDepartmentPerMinute".ToLower()).AsInt32().Nullable();

			if (!Schema.Table(table).Column("RequireLinkingConfirmation".ToLower()).Exists())
				Alter.Table(table).AddColumn("RequireLinkingConfirmation".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true);

			if (!Schema.Table(table).Column("ProactiveNotificationsEnabled".ToLower()).Exists())
				Alter.Table(table).AddColumn("ProactiveNotificationsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
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
