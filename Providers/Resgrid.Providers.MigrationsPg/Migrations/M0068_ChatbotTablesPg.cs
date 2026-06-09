using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(68)]
	public class M0068_ChatbotTablesPg : Migration
	{
		public override void Up()
		{
			// Each table is guarded independently: databases upgraded before the 68->74 migration
			// renumber may already have some of these (created under a prior version), so skip any
			// table that already exists rather than failing the whole migration.

			// ChatbotUserIdentities - Links platform identities to Resgrid users
			if (!Schema.Table("ChatbotUserIdentities".ToLower()).Exists())
			{
				Create.Table("ChatbotUserIdentities".ToLower())
					.WithColumn("Id".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("Platform".ToLower()).AsInt32().NotNullable()
					.WithColumn("PlatformUserId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("PlatformUserName".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IsActive".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedAt".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("LastUsedAt".ToLower()).AsDateTime2().Nullable()
					.WithColumn("LinkingMethod".ToLower()).AsCustom("citext").Nullable();

				Create.Index("IX_ChatbotUserIdentities_User_Platform".ToLower())
					.OnTable("ChatbotUserIdentities".ToLower())
					.OnColumn("UserId".ToLower()).Ascending()
					.OnColumn("Platform".ToLower()).Ascending();

				Create.Index("IX_ChatbotUserIdentities_Platform_PlatformUserId".ToLower())
					.OnTable("ChatbotUserIdentities".ToLower())
					.OnColumn("Platform".ToLower()).Ascending()
					.OnColumn("PlatformUserId".ToLower()).Ascending();
			}

			// ChatbotSessions - Active conversation session state
			if (!Schema.Table("ChatbotSessions".ToLower()).Exists())
			{
				Create.Table("ChatbotSessions".ToLower())
					.WithColumn("SessionId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Platform".ToLower()).AsInt32().NotNullable()
					.WithColumn("State".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PendingIntent".ToLower()).AsInt32().Nullable()
					.WithColumn("ContextJson".ToLower()).AsCustom("text").Nullable()
					.WithColumn("CreatedAt".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("LastActivity".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("TtlMinutes".ToLower()).AsInt32().NotNullable().WithDefaultValue(30);

				Create.Index("IX_ChatbotSessions_UserId_Department".ToLower())
					.OnTable("ChatbotSessions".ToLower())
					.OnColumn("UserId".ToLower()).Ascending()
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("Platform".ToLower()).Ascending();

				Create.Index("IX_ChatbotSessions_LastActivity".ToLower())
					.OnTable("ChatbotSessions".ToLower())
					.OnColumn("LastActivity".ToLower()).Ascending();
			}

			// ChatbotMessageLog - Audit log of all chatbot interactions
			if (!Schema.Table("ChatbotMessageLog".ToLower()).Exists())
			{
				Create.Table("ChatbotMessageLog".ToLower())
					.WithColumn("Id".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SessionId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Platform".ToLower()).AsInt32().NotNullable()
					.WithColumn("Direction".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("MessageText".ToLower()).AsCustom("text").Nullable()
					.WithColumn("IntentType".ToLower()).AsInt32().Nullable()
					.WithColumn("Processed".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ErrorInfo".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Timestamp".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatbotMessageLog_Department_Timestamp".ToLower())
					.OnTable("ChatbotMessageLog".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("Timestamp".ToLower()).Descending();

				Create.Index("IX_ChatbotMessageLog_UserId".ToLower())
					.OnTable("ChatbotMessageLog".ToLower())
					.OnColumn("UserId".ToLower()).Ascending();
			}

			// ChatbotDepartmentConfigs - Per-department chatbot configuration
			if (!Schema.Table("ChatbotDepartmentConfigs".ToLower()).Exists())
			{
				Create.Table("ChatbotDepartmentConfigs".ToLower())
					.WithColumn("Id".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("IsEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NluProvider".ToLower()).AsCustom("citext").NotNullable().WithDefaultValue("keyword")
					.WithColumn("AllowedPlatforms".ToLower()).AsCustom("citext").NotNullable().WithDefaultValue("*")
					.WithColumn("MaxSessionsPerUser".ToLower()).AsInt32().NotNullable().WithDefaultValue(3)
					.WithColumn("SessionTtlMinutes".ToLower()).AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("AllowDispatchViaChatbot".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequireConfirmationForStatusChange".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedAt".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("UpdatedAt".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_ChatbotDepartmentConfigs_DepartmentId".ToLower())
					.OnTable("ChatbotDepartmentConfigs".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("ChatbotDepartmentConfigs".ToLower());
			Delete.Table("ChatbotMessageLog".ToLower());
			Delete.Table("ChatbotSessions".ToLower());
			Delete.Table("ChatbotUserIdentities".ToLower());
		}
	}
}
