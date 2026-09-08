using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Readiness Pro: Stripe USD 150/month, product prod_VDtkPNAa2qNBx3.
	/// No base PlanId: the add-on has an independent monthly interval. Paddle EUR 195/month
	/// is configured in PaymentProviderConfig.PaddleReadinessProAddon (product
	/// pro_01m20xwmzpnkxzp7mm7nwwxp7p). Test IDs are deliberately unset.
	/// </summary>
	[Migration(190)]
	public class M0190_SeedReadinessProAddonPg : Migration
	{
		private const string ReadinessProAddonId = "8a82f517-13db-4950-a514-d990248a67e6";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + ReadinessProAddonId + "', 3, 150, 'price_0UDRwaqJFDZJcnkVnYP8bAcd', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + ReadinessProAddonId + "' OR addontype = 3);");
		}

		public override void Down()
		{
			// Preserve the catalog and any billed references, as in the SQL Server twin.
		}
	}
}
