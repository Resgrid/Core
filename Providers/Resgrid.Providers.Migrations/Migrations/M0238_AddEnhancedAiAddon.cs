using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Enhanced AI add-on purchase foundation (enhanced-ai-addon-plan.md; registry §4E, next physical number under the
	/// no-gaps rule): the PlanAddons catalog row (PlanAddonTypes 5, USD 95/month through Stripe price
	/// price_0UJZXtqJFDZJcnkVbvetyYlx; TestExternalId stays empty so test-mode checkout fails closed — the Paddle EUR 145
	/// price lives in PaymentProviderConfig), the AiBillingAccounts table (mirror of
	/// BusinessOperationsBillingAccounts, M0211), Admin Assist free-allowance columns on the M0237 AiUsageLedger
	/// (Feature, Tier, CreatedOnUtc), and the six Ai.* capability flags, all off, each with a prerequisite on Ai.Enhanced.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(238)]
	public class M0238_AddEnhancedAiAddon : Migration
	{
		private const string EnhancedAiAddonId = "e8bb4e6a-d61b-4654-844f-1afb25b60e93";
		private const string StripePriceId = "price_0UJZXtqJFDZJcnkVbvetyYlx";

		private static readonly (string Key, string Name, string Description)[] CapabilityFlags =
		{
			("Ai.Assistant", "AI assistant", "Enhanced AI: conversational fallback and incident questions over the live command board. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Narratives", "AI drafts", "Enhanced AI: incident report, after-action, ICS-201 briefing and call-brief drafts a person reviews before use. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Knowledge", "AI knowledge answers", "Enhanced AI: department knowledge answers with citations over documents, protocols, notes and pre-plans. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Search", "AI semantic search", "Enhanced AI Phase 2: hybrid semantic search beside the keyword index; needs the ai-dispatch GPU. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Voice", "AI transcription", "Enhanced AI Phase 2: dispatch audio and push-to-talk transcription; needs the ai-dispatch GPU. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off."),
			("Ai.Analytics", "AI analytics questions", "Enhanced AI: plain-language questions over records analytics, answered through typed queries. Requires Ai.Enhanced and the Enhanced AI add-on. Seeded off.")
		};

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + EnhancedAiAddonId + "' OR [AddonType] = 5) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + EnhancedAiAddonId + "', 5, 95, '" + StripePriceId + "', '');");
			// A row seeded before the Stripe price existed carries an empty ExternalId; fill it without touching an operator's value.
			Execute.Sql(
				"UPDATE [PlanAddons] SET [ExternalId] = '" + StripePriceId + "' " +
				"WHERE [PlanAddonId] = '" + EnhancedAiAddonId + "' AND ([ExternalId] IS NULL OR [ExternalId] = '');");

			if (!Schema.Table("AiBillingAccounts").Exists())
			{
				Create.Table("AiBillingAccounts")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("Provider").AsString(20).NotNullable()
					.WithColumn("CustomerId").AsString(100).NotNullable()
					.WithColumn("PlanAddonId").AsString(36).NotNullable()
					.WithColumn("PriceId").AsString(100).NotNullable()
					.WithColumn("SubscriptionId").AsString(100).Nullable()
					.WithColumn("CheckoutId").AsString(100).Nullable()
					.WithColumn("CheckoutUrl").AsString(2048).Nullable()
					.WithColumn("CheckoutExpiresOn").AsDateTime2().Nullable()
					.WithColumn("CheckoutAttempt").AsString(36).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			// Ledger rows written before this migration are paid Admin Assist reservations; NULL Feature/Tier reads as that.
			if (!Schema.Table("AiUsageLedger").Column("Feature").Exists())
				Alter.Table("AiUsageLedger").AddColumn("Feature").AsString(40).Nullable();
			if (!Schema.Table("AiUsageLedger").Column("Tier").Exists())
				Alter.Table("AiUsageLedger").AddColumn("Tier").AsString(20).Nullable();
			if (!Schema.Table("AiUsageLedger").Column("CreatedOnUtc").Exists())
				Alter.Table("AiUsageLedger").AddColumn("CreatedOnUtc").AsDateTime().Nullable();
			if (!Schema.Table("AiUsageLedger").Index("IX_AiUsageLedger_Allowance").Exists())
				Create.Index("IX_AiUsageLedger_Allowance").OnTable("AiUsageLedger")
					.OnColumn("DepartmentId").Ascending().OnColumn("Tier").Ascending().OnColumn("CreatedOnUtc").Ascending();

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Ai.Enhanced') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Ai.Enhanced', 'Enhanced AI', 'Operator rollout; entitlement is checked separately.', 'AI', 0);");

			foreach (var flag in CapabilityFlags)
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + flag.Key + "') " +
					"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
					"VALUES ('" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', 'AI', 0);");

				// Prerequisite edge: the capability requires Ai.Enhanced to be on (RequiredValue null = enabled).
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
					"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
					"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
					"  WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = 'Ai.Enhanced') " +
					"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
					"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
					"WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = 'Ai.Enhanced';");
			}
		}

		public override void Down()
		{
			// Operator-owned rows (add-on catalog, rollout flags, prerequisite edges) and ledger evidence are never removed on
			// rollback; the billing-account table is dropped only when empty.
			Execute.Sql("IF OBJECT_ID('[AiBillingAccounts]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AiBillingAccounts]) DROP TABLE [AiBillingAccounts];");
		}
	}
}
