using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// AI dispatch, Enrich mode (ai-dispatch-template-plan.md; enhanced-ai-addon-plan.md §4; registry §4D, next physical number):
	/// the metadata-only <c>AiDispatchAudits</c> table, whose unique (DepartmentId, CallId) index is the idempotency claim, and
	/// the <c>Dispatch.AiTemplate</c> flag seeded off with a prerequisite on <c>Ai.Enhanced</c>. No message, prompt, reply or
	/// extracted value is stored, so no protected-data catalog entry is needed. Guarded for safe retry.
	/// </summary>
	[Migration(240)]
	public class M0240_AddAiDispatchEnrichment : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AiDispatchAudits").Exists())
			{
				Create.Table("AiDispatchAudits")
					.WithColumn("AiDispatchAuditId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Mode").AsString(20).NotNullable()
					.WithColumn("Outcome").AsString(40).NotNullable()
					.WithColumn("Confidence").AsDecimal(5, 4).Nullable()
					.WithColumn("AppliedFields").AsString(200).Nullable()
					.WithColumn("RejectedCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RelatedCallId").AsInt32().Nullable()
					.WithColumn("PromptVersion").AsString(64).Nullable()
					.WithColumn("ModelName").AsString(128).Nullable()
					.WithColumn("ModelRevision").AsString(64).Nullable()
					.WithColumn("RuntimeDigest").AsString(80).Nullable()
					.WithColumn("InputTokens").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OutputTokens").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LatencyMs").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOnUtc").AsDateTime2().NotNullable()
					.WithColumn("CompletedOnUtc").AsDateTime2().Nullable();
			}

			if (!Schema.Table("AiDispatchAudits").Index("UX_AiDispatchAudits_Call").Exists())
				Create.Index("UX_AiDispatchAudits_Call").OnTable("AiDispatchAudits")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending().WithOptions().Unique();
			if (!Schema.Table("AiDispatchAudits").Index("IX_AiDispatchAudits_Recent").Exists())
				Create.Index("IX_AiDispatchAudits_Recent").OnTable("AiDispatchAudits")
					.OnColumn("DepartmentId").Ascending().OnColumn("CreatedOnUtc").Descending();

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Dispatch.AiTemplate') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Dispatch.AiTemplate', 'AI dispatch', 'AI enrichment of calls created from AI-format dispatch emails (Enrich mode: the call is created and dispatched first). Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.', 'AI', 0);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
				"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
				"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
				"  WHERE f.[FlagKey] = 'Dispatch.AiTemplate' AND r.[FlagKey] = 'Ai.Enhanced') " +
				"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
				"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
				"WHERE f.[FlagKey] = 'Dispatch.AiTemplate' AND r.[FlagKey] = 'Ai.Enhanced';");
		}

		public override void Down()
		{
			// Rollout flags and their prerequisite edge are operator-owned and never removed; the audit table is dropped only when empty.
			Execute.Sql("IF OBJECT_ID('[AiDispatchAudits]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AiDispatchAudits]) DROP TABLE [AiDispatchAudits];");
		}
	}
}
