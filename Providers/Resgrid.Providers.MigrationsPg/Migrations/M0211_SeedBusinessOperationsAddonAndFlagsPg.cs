using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0211 (Workforce &amp; Business Operations plan, Phase B): Business Operations add-on catalog
	/// row, BusinessOperationsBillingAccounts, the Business.Operations and Invoicing.CustomerInvoicing flags, and the
	/// prerequisite edge between them. Same number, lower-case identifiers, guarded for safe retry.
	/// </summary>
	[Migration(211)]
	public class M0211_SeedBusinessOperationsAddonAndFlagsPg : Migration
	{
		private const string BusinessOperationsAddonId = "8c2f0d6e-5b1a-4f2e-9d3c-7a6b5e4d3c2b";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + BusinessOperationsAddonId + "', 4, 250, 'price_0UHEA6qJFDZJcnkVnj0ZaAFw', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + BusinessOperationsAddonId + "' OR addontype = 4);");

			if (!Schema.Table("businessoperationsbillingaccounts").Exists())
			{
				Create.Table("businessoperationsbillingaccounts")
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

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally, ispermanent) " +
				"SELECT 'Business.Operations', 'Business Operations', 'Operator master toggle for the paid Business Operations add-on surfaces (customer invoicing, contractor billing, Cal OES MARS, workforce costing). Prerequisite of every paid Business Operations flag; the purchase itself is the Business Operations add-on. Seeded off.', 'Business', false, true " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Business.Operations');");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Invoicing.CustomerInvoicing', 'Customer invoicing', 'Billing profiles, rate cards, invoices, payments and accounts-receivable aging (Workforce & Business Operations plan, Phase B). Requires Business.Operations and the Business Operations add-on. Seeded off.', 'Business', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Invoicing.CustomerInvoicing');");

			Execute.Sql(
				"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
				"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
				"WHERE f.flagkey = 'Invoicing.CustomerInvoicing' AND r.flagkey = 'Business.Operations' " +
				"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
		}

		public override void Down()
		{
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('businessoperationsbillingaccounts') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM businessoperationsbillingaccounts) THEN DROP TABLE businessoperationsbillingaccounts; END IF; END $guard$;");
		}
	}
}
