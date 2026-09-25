using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(237)]
	public class M0237_AddAdminAssistConversation : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentAiConfigs").Exists())
				Create.Table("DepartmentAiConfigs")
					.WithColumn("DepartmentId").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("Disabled").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("MonthlyTokenLimit").AsInt32().Nullable()
					.WithColumn("Revision").AsInt64().NotNullable();
			if (!Schema.Table("AiAdmission").Exists())
				Create.Table("AiAdmission")
					.WithColumn("Id").AsInt32().PrimaryKey().NotNullable();
			if (!Schema.Table("AdminAssistConversations").Exists())
				Create.Table("AdminAssistConversations")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("ModifiedOnUtc").AsDateTime().NotNullable()
					.WithColumn("Deleted").AsBoolean().WithDefaultValue(false).NotNullable();
			if (!Schema.Table("AiUsageLedger").Exists())
				Create.Table("AiUsageLedger")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Month").AsString(7).NotNullable()
					.WithColumn("ReservedTokens").AsInt32().NotNullable()
					.WithColumn("UsedTokens").AsInt32().Nullable()
					.WithColumn("ExpiresOnUtc").AsDateTime().NotNullable()
					.WithColumn("Outcome").AsString(40).Nullable();
			if (!Schema.Table("AiGenerations").Exists())
				Create.Table("AiGenerations")
					.WithColumn("Id").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("ConversationId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("CreatedOnUtc").AsDateTime().NotNullable()
					.WithColumn("Content").AsString(int.MaxValue).NotNullable()
					.WithColumn("IsProtected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().Nullable()
					.WithColumn("PromptVersion").AsString(64).NotNullable()
					.WithColumn("ModelRevision").AsString(64).NotNullable()
					.WithColumn("RuntimeDigest").AsString(80).NotNullable()
					.WithColumn("RequestDigest").AsString(64).NotNullable()
					.WithColumn("InputTokens").AsInt32().NotNullable()
					.WithColumn("OutputTokens").AsInt32().NotNullable()
					.WithColumn("Outcome").AsString(40).NotNullable();
			if (!Schema.Table("AiUsageLedger").Index("IX_AiUsageLedger_Scope").Exists())
				Create.Index("IX_AiUsageLedger_Scope").OnTable("AiUsageLedger").OnColumn("Month").Ascending().OnColumn("DepartmentId").Ascending();
			if (!Schema.Table("AiGenerations").Index("IX_AiGenerations_Scope").Exists())
				Create.Index("IX_AiGenerations_Scope").OnTable("AiGenerations").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("ConversationId").Ascending().OnColumn("Revision").Ascending();
			if (!Schema.Table("AdminAssistConversations").Index("IX_AdminAssistConversations_Scope").Exists())
				Create.Index("IX_AdminAssistConversations_Scope").OnTable("AdminAssistConversations").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("ModifiedOnUtc").Ascending();
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [AiAdmission] WHERE [Id]=1) INSERT INTO [AiAdmission] ([Id]) VALUES (1);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey]='Ai.Enhanced') INSERT INTO [FeatureFlags] ([FlagKey],[Name],[Description],[Category],[IsEnabledGlobally]) VALUES ('Ai.Enhanced','Enhanced AI','Operator rollout; entitlement is checked separately.','AI',0);");
			Execute.Sql("INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId],[RequiredFeatureFlagId]) SELECT f.[FeatureFlagId],p.[FeatureFlagId] FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] p WHERE f.[FlagKey]='Ai.AdminAssist' AND p.[FlagKey]='Ai.Enhanced' AND NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] r WHERE r.[FeatureFlagId]=f.[FeatureFlagId] AND r.[RequiredFeatureFlagId]=p.[FeatureFlagId]);");
			Execute.Sql("INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId],[RequiredFeatureFlagId]) SELECT f.[FeatureFlagId],p.[FeatureFlagId] FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] p WHERE f.[FlagKey]='Ai.AdminAssist' AND p.[FlagKey]='Admin.Assist' AND NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] r WHERE r.[FeatureFlagId]=f.[FeatureFlagId] AND r.[RequiredFeatureFlagId]=p.[FeatureFlagId]);");
		}
		public override void Down()
		{
			// Protected evidence is removed only through its approved lifecycle.
			throw new System.NotSupportedException("Disable AI rollout; conversation schema rollback requires an approved data migration.");
		}
	}
}
