using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Pack add-on billing accounts (LMS add-on, PlanAddonTypes 9; later Workforce Pro): one row per department and add-on type, the
	/// same shape as DedicatedNumberBillingAccounts (M0323) with PriceId widened to 400 (a Stripe graduated price, or the Paddle tier
	/// prices joined) plus CheckoutPacks (the packs a pending checkout buys) and ScheduledPacks (a decrease waiting for the next billing
	/// date). Written and read by int-CommonApis (BillingApi `PackAddonBillingRepository`); Core owns the schema. No foreign keys, like
	/// the other billing account tables; UnlinkedDepartmentCleanup removes the rows with the department. Existence-guarded for safe retry.
	/// </summary>
	[Migration(413)]
	public class M0413_AddPackAddonBillingAccounts : Migration
	{
		public override void Up()
		{
			if (Schema.Table("PackAddonBillingAccounts").Exists())
				return;

			Create.Table("PackAddonBillingAccounts")
				.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey("PK_PackAddonBillingAccounts")
				.WithColumn("AddonType").AsInt32().NotNullable().PrimaryKey("PK_PackAddonBillingAccounts")
				.WithColumn("Provider").AsString(20).NotNullable()
				.WithColumn("CustomerId").AsString(100).NotNullable()
				.WithColumn("PlanAddonId").AsString(36).NotNullable()
				.WithColumn("PriceId").AsString(400).NotNullable()
				.WithColumn("SubscriptionId").AsString(100).Nullable()
				.WithColumn("CheckoutId").AsString(100).Nullable()
				.WithColumn("CheckoutUrl").AsString(2048).Nullable()
				.WithColumn("CheckoutExpiresOn").AsDateTime2().Nullable()
				.WithColumn("CheckoutAttempt").AsString(36).Nullable()
				.WithColumn("CheckoutPacks").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("ScheduledPacks").AsInt32().Nullable()
				.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
		}

		public override void Down()
		{
			if (Schema.Table("PackAddonBillingAccounts").Exists())
				Delete.Table("PackAddonBillingAccounts");
		}
	}
}
