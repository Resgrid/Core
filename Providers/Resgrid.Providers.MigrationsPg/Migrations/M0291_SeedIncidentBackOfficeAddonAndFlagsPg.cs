using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0291. Incident Back Office add-on and rollout seeds (plan rev 4.1 §17, decision D6; registry §4C): the PlanAddons catalog row for
	/// PlanAddonTypes 6 IncidentBackOffice (cost 250, USD per month through Stripe: product prod_VWFcR3xzTuNhkv, live price
	/// price_0UVD6mqJFDZJcnkVqBlHZD0w; EUR 350 through Paddle is configuration in PaymentProviderConfig). The test-mode Stripe price is
	/// still empty, so test-mode checkout fails closed (the Business Ops M0211 precedent). The IncidentBackOfficeBillingAccounts table mirrors
	/// BusinessOperationsBillingAccounts. Flags Incident.BackOffice (operator master, permanent), Incident.Business and
	/// Incident.Coordination are seeded off, the two children with a FeatureFlagPrerequisites edge to the master. The dead
	/// legacy Incident/IncidentLog pair (D3) is retired in code only: its tables were never created by any migration. Guarded for safe retry.
	/// </summary>
	[Migration(291)]
	public class M0291_SeedIncidentBackOfficeAddonAndFlagsPg : Migration
	{
		private const string IncidentBackOfficeAddonId = "4e7b2c19-6d3a-4f85-b1c0-9a2e5d7f3b61";

		public override void Up()
		{
			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + IncidentBackOfficeAddonId + "', 6, 250, 'price_0UVD6mqJFDZJcnkVqBlHZD0w', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + IncidentBackOfficeAddonId + "' OR addontype = 6);");

			if (!Schema.Table("incidentbackofficebillingaccounts").Exists())
			{
				Create.Table("incidentbackofficebillingaccounts")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("provider").AsCustom("citext").NotNullable()
					.WithColumn("customerid").AsCustom("citext").NotNullable()
					.WithColumn("planaddonid").AsCustom("citext").NotNullable()
					.WithColumn("priceid").AsCustom("citext").NotNullable()
					.WithColumn("subscriptionid").AsCustom("citext").Nullable()
					.WithColumn("checkoutid").AsCustom("citext").Nullable()
					.WithColumn("checkouturl").AsCustom("citext").Nullable()
					.WithColumn("checkoutexpireson").AsDateTime2().Nullable()
					.WithColumn("checkoutattempt").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally, ispermanent) " +
				"SELECT 'Incident.BackOffice', 'Incident Back Office', 'Operator master toggle for the paid Incident Back Office add-on (large-incident-logistics-finance-plan.md, LI-A Foundation and LI-B Logistics: activations, periods, positions, team rosters, participants, requests, check-in, demob, facilities, lodging, feeding, services, ground support, vendors). Prerequisite of Incident.Business and Incident.Coordination; the purchase itself is the Incident Back Office add-on (PlanAddonTypes 6). Seeded off.', 'Incident', false, true " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Incident.BackOffice');");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Incident.Business', 'Incident Back Office business', 'LI-C incident business: procurement, budgets and funding authority, time and equipment use, cost and accruals, cost share, claims, disaster cost recovery, the evidence intake inbox and closeout. Requires Incident.BackOffice and the Incident Back Office add-on. Seeded off.', 'Incident', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Incident.Business');");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT 'Incident.Coordination', 'Incident coordination boards', 'LI-D emergency-management coordination: status boards, lifelines, objectives, open actions, decisions, situational layers and interagency board sharing. Requires Incident.BackOffice and the Incident Back Office add-on. Seeded off.', 'Incident', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Incident.Coordination');");

			Execute.Sql(
				"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
				"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
				"WHERE f.flagkey = 'Incident.Business' AND r.flagkey = 'Incident.BackOffice' " +
				"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");

			Execute.Sql(
				"INSERT INTO featureflagprerequisites (featureflagid, requiredfeatureflagid, requiredvalue) " +
				"SELECT f.featureflagid, r.featureflagid, NULL FROM featureflags f CROSS JOIN featureflags r " +
				"WHERE f.flagkey = 'Incident.Coordination' AND r.flagkey = 'Incident.BackOffice' " +
				"AND NOT EXISTS (SELECT 1 FROM featureflagprerequisites p WHERE p.featureflagid = f.featureflagid AND p.requiredfeatureflagid = r.featureflagid);");
		}

		public override void Down()
		{
			Execute.Sql("DO $guard$ BEGIN IF to_regclass('incidentbackofficebillingaccounts') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM incidentbackofficebillingaccounts) THEN DROP TABLE incidentbackofficebillingaccounts; END IF; END $guard$;");
		}
	}
}
