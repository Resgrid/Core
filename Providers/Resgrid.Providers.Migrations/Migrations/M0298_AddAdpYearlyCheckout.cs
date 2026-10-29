using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Advanced Data Protection yearly checkout (AdpAddonConfig). ADP is now bought as its own yearly subscription through the
	/// Billing API's AdpBilling backend instead of as an item on the department's base Stripe subscription, so it gets:
	/// 1. A second ADP PlanAddons row (PlanAddonTypes 2, USD 1,950/yr, resgrid.com/pricing) whose intervals that backend owns
	///    (PlanAddon.IsDedicatedAddon). ExternalId / TestExternalId stay empty until the USD 1,950 yearly Stripe prices exist, so
	///    checkout fails closed; the EUR 2,550 Paddle price lives in PaymentProviderConfig.PaddleAdpAddon.
	/// 2. The AdpBillingAccounts table (mirror of AiBillingAccounts, M0238).
	/// The legacy M0126 row (USD 999) is left as it is: departments that bought the subscription item still pay that price, and the
	/// shared billing paths still map their renewals by it. Guarded for safe retry.
	/// </summary>
	[Migration(298)]
	public class M0298_AddAdpYearlyCheckout : Migration
	{
		// Keep in sync with Resgrid.Config.AdpAddonConfig.YearlyPlanAddonId.
		private const string AdpYearlyPlanAddonId = "6d1f0c3a-9b7e-4e52-8a1d-3f2c7b9e5a61";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + AdpYearlyPlanAddonId + "') " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + AdpYearlyPlanAddonId + "', 2, 1950, '', '');");

			if (!Schema.Table("AdpBillingAccounts").Exists())
			{
				Create.Table("AdpBillingAccounts")
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
		}

		public override void Down()
		{
			// The table holds provider routing for paid subscriptions and the catalog row may be referenced by PaymentAddons rows;
			// neither is safe to drop automatically.
		}
	}
}
