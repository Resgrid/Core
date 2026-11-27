using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Sets the live Stripe graduated price of the LMS add-on (PlanAddonTypes 9, product prod_VhZ2gsR0yP3sp4, set 2026-11-26) on its
	/// PlanAddons row (M0351 seeded ExternalId empty, so checkout failed closed). Writes only when ExternalId is still empty, so an
	/// operator's own value is never overwritten. The test-mode price (TestExternalId) stays empty until a Stripe test-mode price
	/// exists. Down clears it only if it is still this price.
	/// </summary>
	[Migration(420)]
	public class M0420_SetLmsAddonStripePrice : Migration
	{
		private const string LmsAddonId = "7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93";

		/// <summary>Live Stripe price: monthly, tiered graduated, $79 packs 1–4 / $49 packs 5–20 / $29 packs 21+.</summary>
		public const string StripePriceId = "price_0Ug9uDqJFDZJcnkVpXHfHWb0";

		public override void Up()
		{
			Execute.Sql(
				"UPDATE [PlanAddons] SET [ExternalId] = '" + StripePriceId + "' " +
				"WHERE [PlanAddonId] = '" + LmsAddonId + "' AND ([ExternalId] IS NULL OR [ExternalId] = '');");
		}

		public override void Down()
		{
			Execute.Sql(
				"UPDATE [PlanAddons] SET [ExternalId] = '' " +
				"WHERE [PlanAddonId] = '" + LmsAddonId + "' AND [ExternalId] = '" + StripePriceId + "';");
		}
	}
}
