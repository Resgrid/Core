using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0298: the Advanced Data Protection yearly checkout's PlanAddons row (PlanAddonTypes 2, USD 1,950/yr,
	/// empty price ids so checkout fails closed until the Stripe prices exist) and adpbillingaccounts (mirror of aibillingaccounts).
	/// The legacy M0126 row is left as it is. Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(298)]
	public class M0298_AddAdpYearlyCheckoutPg : Migration
	{
		// Keep in sync with Resgrid.Config.AdpAddonConfig.YearlyPlanAddonId.
		private const string AdpYearlyPlanAddonId = "6d1f0c3a-9b7e-4e52-8a1d-3f2c7b9e5a61";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + AdpYearlyPlanAddonId + "', 2, 1950, '', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + AdpYearlyPlanAddonId + "');");

			if (!Schema.Table("adpbillingaccounts").Exists())
			{
				Create.Table("adpbillingaccounts")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("provider").AsString(20).NotNullable()
					.WithColumn("customerid").AsString(100).NotNullable()
					.WithColumn("planaddonid").AsString(36).NotNullable()
					.WithColumn("priceid").AsString(100).NotNullable()
					.WithColumn("subscriptionid").AsString(100).Nullable()
					.WithColumn("checkoutid").AsString(100).Nullable()
					.WithColumn("checkouturl").AsString(2048).Nullable()
					.WithColumn("checkoutexpireson").AsDateTime2().Nullable()
					.WithColumn("checkoutattempt").AsString(36).Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}
		}

		public override void Down()
		{
			// The table holds provider routing for paid subscriptions and the catalog row may be referenced by paymentaddons rows;
			// neither is safe to drop automatically.
		}
	}
}
