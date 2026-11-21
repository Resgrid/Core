using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0327: the Advanced Data Protection yearly PlanAddons row (seeded by M0298) moves to the USD 3,600/yr live
	/// Stripe price and catalog cost. TestExternalId, the EUR 2,550 Paddle price and the legacy M0126 row are untouched.
	/// Same number, lower-case identifiers; re-running sets the same values.
	/// </summary>
	[Migration(327)]
	public class M0327_UpdateAdpYearlyPricePg : Migration
	{
		// Keep in sync with Resgrid.Config.AdpAddonConfig.YearlyPlanAddonId.
		private const string AdpYearlyPlanAddonId = "6d1f0c3a-9b7e-4e52-8a1d-3f2c7b9e5a61";
		private const string StripeYearlyPriceId = "price_0UeKlAqJFDZJcnkVEuoFZ6v0";

		public override void Up()
		{
			Execute.Sql(
				"UPDATE planaddons SET cost = 3600, externalid = '" + StripeYearlyPriceId + "' " +
				"WHERE planaddonid = '" + AdpYearlyPlanAddonId + "';");
		}

		public override void Down()
		{
			// Departments may already have checked out against the new price (adpbillingaccounts.priceid); reverting the catalog row
			// would block their renewals and re-checkouts, so it is never done automatically.
		}
	}
}
