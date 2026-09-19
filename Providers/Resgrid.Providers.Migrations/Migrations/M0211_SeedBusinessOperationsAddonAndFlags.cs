using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase B (B1, decision 42): the Business Operations add-on catalog row
	/// (PlanAddonTypes 4, USD 250/month through Stripe: product prod_VHnlBsvKsSpqeP, live price price_0UHEA6qJFDZJcnkVnj0ZaAFw;
	/// the test-mode price id is empty until a test product exists — Readiness Pro M0190 precedent), the
	/// BusinessOperationsBillingAccounts table (mirror of
	/// ReadinessProBillingAccounts, M0197), the operator master flag Business.Operations and the Phase B flag
	/// Invoicing.CustomerInvoicing (both off), and the first FeatureFlagPrerequisites row in the repository:
	/// Invoicing.CustomerInvoicing requires Business.Operations (plan decision 11). Registry M0211. Guarded for safe retry.
	/// </summary>
	[Migration(211)]
	public class M0211_SeedBusinessOperationsAddonAndFlags : Migration
	{
		private const string BusinessOperationsAddonId = "8c2f0d6e-5b1a-4f2e-9d3c-7a6b5e4d3c2b";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + BusinessOperationsAddonId + "' OR [AddonType] = 4) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + BusinessOperationsAddonId + "', 4, 250, 'price_0UHEA6qJFDZJcnkVnj0ZaAFw', '');");

			if (!Schema.Table("BusinessOperationsBillingAccounts").Exists())
			{
				Create.Table("BusinessOperationsBillingAccounts")
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

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Business.Operations') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally], [IsPermanent]) " +
				"VALUES ('Business.Operations', 'Business Operations', 'Operator master toggle for the paid Business Operations add-on surfaces (customer invoicing, contractor billing, Cal OES MARS, workforce costing). Prerequisite of every paid Business Operations flag; the purchase itself is the Business Operations add-on. Seeded off.', 'Business', 0, 1);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Invoicing.CustomerInvoicing') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Invoicing.CustomerInvoicing', 'Customer invoicing', 'Billing profiles, rate cards, invoices, payments and accounts-receivable aging (Workforce & Business Operations plan, Phase B). Requires Business.Operations and the Business Operations add-on. Seeded off.', 'Business', 0);");

			// Prerequisite edge: Invoicing.CustomerInvoicing requires Business.Operations to be on (RequiredValue null = enabled).
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
				"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
				"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
				"  WHERE f.[FlagKey] = 'Invoicing.CustomerInvoicing' AND r.[FlagKey] = 'Business.Operations') " +
				"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
				"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
				"WHERE f.[FlagKey] = 'Invoicing.CustomerInvoicing' AND r.[FlagKey] = 'Business.Operations';");
		}

		public override void Down()
		{
			// Operator-owned rows (add-on catalog, rollout flags, prerequisite edge) are never removed on rollback;
			// the billing-account table is dropped only when empty.
			Execute.Sql("IF OBJECT_ID('[BusinessOperationsBillingAccounts]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [BusinessOperationsBillingAccounts]) DROP TABLE [BusinessOperationsBillingAccounts];");
		}
	}
}
