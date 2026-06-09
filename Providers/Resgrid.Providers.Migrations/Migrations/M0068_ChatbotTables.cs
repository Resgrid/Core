using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(68)]
	public class M0068_ChatbotTables : Migration
	{
		public override void Up()
		{
			// Each table is guarded independently: databases upgraded before the 68->74 migration
			// renumber may already have some of these (created under a prior version), so skip any
			// table that already exists rather than failing the whole migration.

			// ChatbotUserIdentities - Links platform identities to Resgrid users
			if (!Schema.Table("ChatbotUserIdentities").Exists())
			{
				Create.Table("ChatbotUserIdentities")
					.WithColumn("Id").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("UserId").AsString(450).NotNullable()
					.WithColumn("Platform").AsInt32().NotNullable()
					.WithColumn("PlatformUserId").AsString(256).NotNullable()
					.WithColumn("PlatformUserName").AsString(256).Nullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("LastUsedAt").AsDateTime2().Nullable()
					.WithColumn("LinkingMethod").AsString(50).Nullable();

				Create.Index("IX_ChatbotUserIdentities_User_Platform")
					.OnTable("ChatbotUserIdentities")
					.OnColumn("UserId").Ascending()
					.OnColumn("Platform").Ascending();

				Create.Index("IX_ChatbotUserIdentities_Platform_PlatformUserId")
					.OnTable("ChatbotUserIdentities")
					.OnColumn("Platform").Ascending()
					.OnColumn("PlatformUserId").Ascending();
			}

			// ChatbotSessions - Active conversation session state
			if (!Schema.Table("ChatbotSessions").Exists())
			{
				Create.Table("ChatbotSessions")
					.WithColumn("SessionId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("UserId").AsString(450).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Platform").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PendingIntent").AsInt32().Nullable()
					.WithColumn("ContextJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("LastActivity").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("TtlMinutes").AsInt32().NotNullable().WithDefaultValue(30);

				Create.Index("IX_ChatbotSessions_UserId_Department")
					.OnTable("ChatbotSessions")
					.OnColumn("UserId").Ascending()
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Platform").Ascending();

				Create.Index("IX_ChatbotSessions_LastActivity")
					.OnTable("ChatbotSessions")
					.OnColumn("LastActivity").Ascending();
			}

			// ChatbotMessageLog - Audit log of all chatbot interactions
			if (!Schema.Table("ChatbotMessageLog").Exists())
			{
				Create.Table("ChatbotMessageLog")
					.WithColumn("Id").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("SessionId").AsString(128).Nullable()
					.WithColumn("Platform").AsInt32().NotNullable()
					.WithColumn("Direction").AsString(10).NotNullable()
					.WithColumn("MessageText").AsString(int.MaxValue).Nullable()
					.WithColumn("IntentType").AsInt32().Nullable()
					.WithColumn("Processed").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ErrorInfo").AsString(500).Nullable()
					.WithColumn("Timestamp").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatbotMessageLog_Department_Timestamp")
					.OnTable("ChatbotMessageLog")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Timestamp").Descending();

				Create.Index("IX_ChatbotMessageLog_UserId")
					.OnTable("ChatbotMessageLog")
					.OnColumn("UserId").Ascending();
			}

			// ChatbotDepartmentConfigs - Per-department chatbot configuration
			if (!Schema.Table("ChatbotDepartmentConfigs").Exists())
			{
				Create.Table("ChatbotDepartmentConfigs")
					.WithColumn("Id").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NluProvider").AsString(50).NotNullable().WithDefaultValue("keyword")
					.WithColumn("AllowedPlatforms").AsString(500).NotNullable().WithDefaultValue("*")
					.WithColumn("MaxSessionsPerUser").AsInt32().NotNullable().WithDefaultValue(3)
					.WithColumn("SessionTtlMinutes").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("AllowDispatchViaChatbot").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequireConfirmationForStatusChange").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("UpdatedAt").AsDateTime2().Nullable();

				Create.Index("IX_ChatbotDepartmentConfigs_DepartmentId")
					.OnTable("ChatbotDepartmentConfigs")
					.OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("ChatbotDepartmentConfigs");
			Delete.Table("ChatbotMessageLog");
			Delete.Table("ChatbotSessions");
			Delete.Table("ChatbotUserIdentities");
		}
	}
}
