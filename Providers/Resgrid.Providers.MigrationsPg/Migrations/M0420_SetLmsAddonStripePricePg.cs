using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0420_SetLmsAddonStripePrice: the live Stripe graduated price of the LMS add-on (product
	/// prod_VhZ2gsR0yP3sp4) on its planaddons row, only while externalid is still empty. Down clears it only if it is still this price.
	/// </summary>
	[Migration(420)]
	public class M0420_SetLmsAddonStripePricePg : Migration
	{
		private const string LmsAddonId = "7d4e2b91-3c58-4f0a-9e16-5b8a2c4d7f93";

		public const string StripePriceId = "price_0Ug9uDqJFDZJcnkVpXHfHWb0";

		public override void Up()
		{
			Execute.Sql(
				"UPDATE planaddons SET externalid = '" + StripePriceId + "' " +
				"WHERE planaddonid = '" + LmsAddonId + "' AND (externalid IS NULL OR externalid = '');");
		}

		public override void Down()
		{
			Execute.Sql(
				"UPDATE planaddons SET externalid = '' " +
				"WHERE planaddonid = '" + LmsAddonId + "' AND externalid = '" + StripePriceId + "';");
		}
	}
}
