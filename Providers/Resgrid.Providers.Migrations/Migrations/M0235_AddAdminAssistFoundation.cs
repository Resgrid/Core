using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>Registry §4G: deterministic setup, evidence review and trace storage. No model payloads.</summary>
	[Migration(235)]
	public class M0235_AddAdminAssistFoundation : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AdminAssistWorkspaces").Exists())
				Create.Table("AdminAssistWorkspaces")
					.WithColumn("DepartmentId").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("Mode").AsInt32().NotNullable()
					.WithColumn("AreasJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("CatalogVersion").AsString(64).NotNullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();
			if (!Schema.Table("AdminAssistLearning").Exists())
				Create.Table("AdminAssistLearning")
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("CapabilityId").AsString(128).NotNullable()
					.WithColumn("CatalogVersion").AsString(64).NotNullable()
					.WithColumn("Learned").AsBoolean().NotNullable()
					.WithColumn("Interested").AsBoolean().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();
			if (!Schema.Table("AdminAssistHistory").Exists())
				Create.Table("AdminAssistHistory")
					.WithColumn("AdminAssistHistoryId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ActorId").AsString(128).Nullable()
					.WithColumn("OccurredOnUtc").AsDateTime2().NotNullable()
					.WithColumn("Source").AsString(64).NotNullable()
					.WithColumn("CorrelationId").AsString(64).Nullable()
					.WithColumn("Action").AsString(64).NotNullable()
					.WithColumn("SubjectId").AsString(192).NotNullable()
					.WithColumn("BeforeCode").AsString(int.MaxValue).Nullable()
					.WithColumn("AfterCode").AsString(int.MaxValue).Nullable()
					.WithColumn("Revision").AsInt64().NotNullable();
			if (!Schema.Table("AdminAssistConfigurationRevisions").Exists())
				Create.Table("AdminAssistConfigurationRevisions")
					.WithColumn("DepartmentId").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();
			if (!Schema.Table("AdminAssistFindings").Exists())
				Create.Table("AdminAssistFindings")
					.WithColumn("AdminAssistFindingId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RuleId").AsString(128).NotNullable()
					.WithColumn("SubjectId").AsString(128).NotNullable()
					.WithColumn("Episode").AsInt32().NotNullable()
					.WithColumn("Result").AsInt32().NotNullable()
					.WithColumn("Severity").AsInt32().NotNullable()
					.WithColumn("ReviewStatus").AsInt32().NotNullable()
					.WithColumn("OwnerId").AsString(128).Nullable()
					.WithColumn("ReviewOn").AsDateTime2().Nullable()
					.WithColumn("ExceptionUntil").AsDateTime2().Nullable()
					.WithColumn("Content").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().WithDefaultValue(0).NotNullable()
					.WithColumn("SnapshotRevision").AsString(128).NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("FirstObservedOn").AsDateTime2().NotNullable()
					.WithColumn("LastObservedOn").AsDateTime2().NotNullable();
			if (!Schema.Table("AdminAssistDispatchTraces").Exists())
				Create.Table("AdminAssistDispatchTraces")
					.WithColumn("AdminAssistDispatchTraceId").AsString(36).PrimaryKey().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("AttemptId").AsString(36).NotNullable()
					.WithColumn("Stage").AsString(48).NotNullable()
					.WithColumn("ResolverVersion").AsString(64).NotNullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("Content").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().WithDefaultValue(false).NotNullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().WithDefaultValue(0).NotNullable();
			if (!Schema.Table("AdminAssistWorkerStates").Exists())
				Create.Table("AdminAssistWorkerStates")
					.WithColumn("DepartmentId").AsInt32().PrimaryKey().NotNullable()
					.WithColumn("LastEvaluatedOn").AsDateTime2().Nullable()
					.WithColumn("LastAttemptOn").AsDateTime2().Nullable()
					.WithColumn("LastDigestOn").AsDateTime2().Nullable()
					.WithColumn("DigestCursor").AsString(128).Nullable()
					.WithColumn("LeaseOwner").AsString(36).Nullable()
					.WithColumn("LeaseExpiresOn").AsDateTime2().Nullable();
			if (!Schema.Table("AdminAssistDailySummaries").Exists())
				Create.Table("AdminAssistDailySummaries")
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DayUtc").AsDateTime2().NotNullable()
					.WithColumn("FailedCount").AsInt32().NotNullable()
					.WithColumn("UnknownCount").AsInt32().NotNullable()
					.WithColumn("EvaluatedCount").AsInt32().NotNullable()
					.WithColumn("CatalogVersion").AsString(64).NotNullable();
			if (!Schema.Table("AdminAssistPreferences").Exists())
				Create.Table("AdminAssistPreferences")
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("DigestEnabled").AsBoolean().NotNullable()
					.WithColumn("QuietStartHour").AsInt32().NotNullable()
					.WithColumn("QuietEndHour").AsInt32().NotNullable()
					.WithColumn("Locale").AsString(16).NotNullable()
					.WithColumn("Revision").AsInt64().NotNullable()
					.WithColumn("LastAttemptWeek").AsString(10).Nullable()
					.WithColumn("LastAttemptOn").AsDateTime2().Nullable()
					.WithColumn("LastAttemptOutcome").AsString(32).Nullable();
			if (!Schema.Table("AdminAssistPreferences").Index("UX_AdminAssistPreferences_Scope").Exists())
				Create.Index("UX_AdminAssistPreferences_Scope").OnTable("AdminAssistPreferences").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().WithOptions().Unique();
			if (!Schema.Table("AdminAssistLearning").Index("UX_AdminAssistLearning_Scope").Exists())
				Create.Index("UX_AdminAssistLearning_Scope").OnTable("AdminAssistLearning").OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("CapabilityId").Ascending().OnColumn("CatalogVersion").Ascending().WithOptions().Unique();
			if (!Schema.Table("AdminAssistHistory").Index("IX_AdminAssistHistory_Scope").Exists())
				Create.Index("IX_AdminAssistHistory_Scope").OnTable("AdminAssistHistory").OnColumn("DepartmentId").Ascending().OnColumn("OccurredOnUtc").Ascending().OnColumn("AdminAssistHistoryId").Ascending();
			if (!Schema.Table("AdminAssistFindings").Index("UX_AdminAssistFindings_Scope").Exists())
				Create.Index("UX_AdminAssistFindings_Scope").OnTable("AdminAssistFindings").OnColumn("DepartmentId").Ascending().OnColumn("RuleId").Ascending().OnColumn("SubjectId").Ascending().WithOptions().Unique();
			if (!Schema.Table("AdminAssistDispatchTraces").Index("IX_AdminAssistDispatchTraces_Scope").Exists())
				Create.Index("IX_AdminAssistDispatchTraces_Scope").OnTable("AdminAssistDispatchTraces").OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending().OnColumn("OccurredOn").Ascending();
			if (!Schema.Table("AdminAssistDailySummaries").Index("UX_AdminAssistDailySummaries_Scope").Exists())
				Create.Index("UX_AdminAssistDailySummaries_Scope").OnTable("AdminAssistDailySummaries").OnColumn("DepartmentId").Ascending().OnColumn("DayUtc").Ascending().WithOptions().Unique();
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey]='Admin.Setup') INSERT INTO [FeatureFlags] ([FlagKey],[Name],[Description],[Category],[IsEnabledGlobally]) VALUES ('Admin.Setup','Department Setup','Seeded off; deterministic setup is independent of AI.','Administration',0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey]='Admin.Assist') INSERT INTO [FeatureFlags] ([FlagKey],[Name],[Description],[Category],[IsEnabledGlobally]) VALUES ('Admin.Assist','Admin Assist','Seeded off; deterministic setup is independent of AI.','Administration',0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey]='Ai.AdminAssist') INSERT INTO [FeatureFlags] ([FlagKey],[Name],[Description],[Category],[IsEnabledGlobally]) VALUES ('Ai.AdminAssist','Admin Assist Conversation','Seeded off; deterministic setup is independent of AI.','Administration',0);");
		}

		public override void Down()
		{
			// Keep evidence and operator-owned flags on rollback; disable rollout instead of deleting history.
		}
	}
}
