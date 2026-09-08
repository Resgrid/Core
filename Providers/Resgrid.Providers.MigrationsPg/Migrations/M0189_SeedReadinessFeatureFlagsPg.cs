using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(189)]
	public class M0189_SeedReadinessFeatureFlagsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Checklists.System', 'Checklists', 'Free checklists for all departments. Independent of paid plans and Readiness Pro. Seeded off.', 'Readiness', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Checklists.System');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Maintenance.WorkOrders', 'Readiness Pro', 'Maintenance and work orders rollout gate. Requires a separate active monthly Readiness Pro entitlement. Independent of Checklists.System. Seeded off.', 'Readiness', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Maintenance.WorkOrders');");
		}

		public override void Down()
		{
			// Preserve operator-owned settings, as in the SQL Server twin.
		}
	}
}
