using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Seeds the Advanced Data Protection (ADP) commercialization rows:
	/// 1. The "Security.DepartmentProtectedDataEnrollment" feature flag — the global enrollment
	///    admission gate. Seeded OFF, permanent and operator-managed; it gates NEW enrollment only and
	///    is never consulted for runtime crypto, grants, rotation, or opt-out of already-enabled
	///    departments. No percentage rollout or targeting is ever applied to this key.
	/// 2. The yearly single-tier ADP PlanAddon row (PlanAddonTypes.ADP = 2, $999/yr launch price).
	///    ExternalId carries the Stripe yearly price id (under product prod_V9NRrdSq5hxCk8); M0136
///    repoints existing rows when that price id changes.
	///    TestExternalId seeds empty until the Stripe test-mode counterpart exists. The Paddle
	///    price id is NOT stored here — per the PTT precedent it lives in
	///    PaymentProviderConfig.PaddleAdpAddon (pri_01m11vm50c17z0rxcgy4fppf80, product
	///    pro_01m11vjn9cjmgmwzgv2kt8wndk).
	/// </summary>
	[Migration(126)]
	public class M0126_SeedAdpFeatureFlagAndAddonPg : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.DepartmentProtectedDataEnrollment.
		private const string FlagKey = "Security.DepartmentProtectedDataEnrollment";

		// Fixed id so every data center's row matches (same convention as the M0023 PTT addon row).
		private const string AdpPlanAddonId = "b3a4f9d2-6c1e-4f8a-9d27-5e9c1b7a4a02";

		public override void Up()
		{
			// Guarded with WHERE NOT EXISTS so re-running the migration does not violate the unique
			// flagkey index. ispermanent = true: the generic admin UI must not archive or delete this
			// key. The identity PK is omitted so Postgres assigns it.
			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally, ispermanent) " +
				"SELECT '" + FlagKey + "', " +
				"'ADP Enrollment Admission', " +
				"'Global admission gate for Advanced Data Protection enrollment. When on, departments with an active paid ADP addon may enroll via the wizard; when off, no new enrollment commits anywhere. Gates new enrollment only - never runtime crypto, grants, rotation or opt-out of enabled departments. Operator-managed; no percentage rollout or targeting.', " +
				"'Security', false, true " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");

			Execute.Sql(
				"INSERT INTO planaddons (planaddonid, addontype, cost, externalid, testexternalid) " +
				"SELECT '" + AdpPlanAddonId + "', 2, 999, 'price_0U9wyZqJFDZJcnkVfFDTHoHl', '' " +
				"WHERE NOT EXISTS (SELECT 1 FROM planaddons WHERE planaddonid = '" + AdpPlanAddonId + "');");
		}

		public override void Down()
		{
			// Deliberate no-op. Up() tolerates pre-existing rows (WHERE NOT EXISTS), so this
			// migration cannot prove it inserted them — deleting by key on rollback could destroy an
			// operator-created enrollment gate or a live, billed addon row. The flag is also seeded
			// ispermanent: nothing may delete it. Re-applying Up() after a rollback is a no-op.
		}
	}
}
