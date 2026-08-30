using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Repoints the ADP PlanAddon row at the current Stripe yearly price id. M0126 seeded the
	/// original id; that price was replaced before launch, and any environment that already ran
	/// M0126 holds the stale value, so the seed edit alone would only fix fresh installs.
	///
	/// The UPDATE is deliberately guarded on the OLD value: an operator who has already pointed a
	/// data center at a different price (a regional entity, a negotiated price) must not have that
	/// choice silently overwritten by a migration. Anything other than the seeded id is left alone.
	/// </summary>
	[Migration(136)]
	public class M0136_UpdateAdpAddonStripePrice : Migration
	{
		// Same fixed id M0126 seeds.
		private const string AdpPlanAddonId = "b3a4f9d2-6c1e-4f8a-9d27-5e9c1b7a4a02";

		private const string OldPriceId = "price_0U94gcqJFDZJcnkVOJNe9SnR";
		private const string NewPriceId = "price_0U9wyZqJFDZJcnkVfFDTHoHl";

		public override void Up()
		{
			Execute.Sql(
				"UPDATE [PlanAddons] SET [ExternalId] = '" + NewPriceId + "' " +
				"WHERE [PlanAddonId] = '" + AdpPlanAddonId + "' AND [ExternalId] = '" + OldPriceId + "';");
		}

		public override void Down()
		{
			// Symmetric and equally guarded: only a row still carrying the new id goes back.
			Execute.Sql(
				"UPDATE [PlanAddons] SET [ExternalId] = '" + OldPriceId + "' " +
				"WHERE [PlanAddonId] = '" + AdpPlanAddonId + "' AND [ExternalId] = '" + NewPriceId + "';");
		}
	}
}
