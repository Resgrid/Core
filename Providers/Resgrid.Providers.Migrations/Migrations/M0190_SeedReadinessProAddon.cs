using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Readiness Pro: Stripe USD 150/month, product prod_VDtkPNAa2qNBx3.
	/// No base PlanId: the add-on has an independent monthly interval. Paddle EUR 195/month
	/// is configured in PaymentProviderConfig.PaddleReadinessProAddon (product
	/// pro_01m20xwmzpnkxzp7mm7nwwxp7p). Test IDs are deliberately unset.
	/// </summary>
	[Migration(190)]
	public class M0190_SeedReadinessProAddon : Migration
	{
		private const string ReadinessProAddonId = "8a82f517-13db-4950-a514-d990248a67e6";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + ReadinessProAddonId + "' OR [AddonType] = 3) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + ReadinessProAddonId + "', 3, 150, 'price_0UDRwaqJFDZJcnkVnYP8bAcd', '');");
		}

		public override void Down()
		{
			// Do not remove an operator-owned or potentially billed row on rollback. Up preserves
			// pre-existing Readiness Pro catalogs and safely tolerates reapplication.
		}
	}
}
