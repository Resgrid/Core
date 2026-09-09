using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(189)]
	public class M0189_SeedReadinessFeatureFlags : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Checklists.System') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Checklists.System', 'Checklists', 'Free checklists for all departments. Independent of paid plans and Readiness Pro. Seeded off.', 'Readiness', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Maintenance.WorkOrders') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Maintenance.WorkOrders', 'Readiness Pro', 'Maintenance and work orders rollout gate. Requires a separate active monthly Readiness Pro entitlement. Independent of Checklists.System. Seeded off.', 'Readiness', 0);");
		}

		public override void Down()
		{
			// Preserve operator-owned rollout settings/overrides. Up tolerates existing keys,
			// so rollback cannot prove ownership of these rows. Reapplying Up is safe.
		}
	}
}
