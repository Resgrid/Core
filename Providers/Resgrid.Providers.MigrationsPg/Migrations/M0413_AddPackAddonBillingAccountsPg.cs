using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0413_AddPackAddonBillingAccounts: the pack add-on billing accounts (LMS add-on, later Workforce Pro), keyed by
	/// department and add-on type, the same shape as dedicatednumberbillingaccounts (M0323) with priceid widened to 400 plus
	/// checkoutpacks and scheduledpacks. No foreign keys; UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(413)]
	public class M0413_AddPackAddonBillingAccountsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("packaddonbillingaccounts").Exists())
				return;

			Create.Table("packaddonbillingaccounts")
				.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey("pk_packaddonbillingaccounts")
				.WithColumn("addontype").AsInt32().NotNullable().PrimaryKey("pk_packaddonbillingaccounts")
				.WithColumn("provider").AsString(20).NotNullable()
				.WithColumn("customerid").AsString(100).NotNullable()
				.WithColumn("planaddonid").AsString(36).NotNullable()
				.WithColumn("priceid").AsString(400).NotNullable()
				.WithColumn("subscriptionid").AsString(100).Nullable()
				.WithColumn("checkoutid").AsString(100).Nullable()
				.WithColumn("checkouturl").AsString(2048).Nullable()
				.WithColumn("checkoutexpireson").AsDateTime2().Nullable()
				.WithColumn("checkoutattempt").AsString(36).Nullable()
				.WithColumn("checkoutpacks").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("scheduledpacks").AsInt32().Nullable()
				.WithColumn("updatedon").AsDateTime2().NotNullable();
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS packaddonbillingaccounts;");
		}
	}
}
