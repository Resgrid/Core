using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Advanced Data Protection yearly checkout price change (2026-11-21): USD 1,950/yr to USD 3,600/yr, now that the add-on is the
	/// prerequisite for Resgrid signing a HIPAA Business Associate Agreement. Points the yearly PlanAddons row seeded by M0298 at the
	/// live Stripe price and sets its catalog cost to match AdpAddonConfig.StripeYearlyAmount (the Billing API validates the provider
	/// price against that amount and fails closed when they differ). TestExternalId is left as it is: a missing sandbox price must
	/// never fall back to production. The EUR 2,550 Paddle price (PaymentProviderConfig.PaddleAdpAddon) is unchanged. The legacy
	/// M0126 row (USD 999) is untouched. Re-running sets the same values.
	/// </summary>
	[Migration(327)]
	public class M0327_UpdateAdpYearlyPrice : Migration
	{
		// Keep in sync with Resgrid.Config.AdpAddonConfig.YearlyPlanAddonId.
		private const string AdpYearlyPlanAddonId = "6d1f0c3a-9b7e-4e52-8a1d-3f2c7b9e5a61";
		private const string StripeYearlyPriceId = "price_0UeKlAqJFDZJcnkVEuoFZ6v0";

		public override void Up()
		{
			Execute.Sql(
				"UPDATE [PlanAddons] SET [Cost] = 3600, [ExternalId] = '" + StripeYearlyPriceId + "' " +
				"WHERE [PlanAddonId] = '" + AdpYearlyPlanAddonId + "';");
		}

		public override void Down()
		{
			// Departments may already have checked out against the new price (AdpBillingAccounts.PriceId); reverting the catalog row
			// would block their renewals and re-checkouts, so it is never done automatically.
		}
	}
}
