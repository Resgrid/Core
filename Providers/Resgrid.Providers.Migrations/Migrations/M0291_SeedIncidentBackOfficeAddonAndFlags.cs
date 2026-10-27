using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Incident Back Office add-on and rollout seeds (plan rev 4.1 §17, decision D6; registry §4C): the PlanAddons catalog row for
	/// PlanAddonTypes 6 IncidentBackOffice (cost 250, USD per month through Stripe: product prod_VWFcR3xzTuNhkv, live price
	/// price_0UVD6mqJFDZJcnkVqBlHZD0w; EUR 350 through Paddle is configuration in PaymentProviderConfig). The test-mode Stripe price is
	/// still empty, so test-mode checkout fails closed (the Business Ops M0211 precedent). The IncidentBackOfficeBillingAccounts table mirrors
	/// BusinessOperationsBillingAccounts. Flags Incident.BackOffice (operator master, permanent), Incident.Business and
	/// Incident.Coordination are seeded off, the two children with a FeatureFlagPrerequisites edge to the master. The dead
	/// legacy Incident/IncidentLog pair (D3) is retired in code only: its tables were never created by any migration. Guarded for safe retry.
	/// </summary>
	[Migration(291)]
	public class M0291_SeedIncidentBackOfficeAddonAndFlags : Migration
	{
		private const string IncidentBackOfficeAddonId = "4e7b2c19-6d3a-4f85-b1c0-9a2e5d7f3b61";

		public override void Up()
		{
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [PlanAddons] WHERE [PlanAddonId] = '" + IncidentBackOfficeAddonId + "' OR [AddonType] = 6) " +
				"INSERT INTO [PlanAddons] ([PlanAddonId], [AddonType], [Cost], [ExternalId], [TestExternalId]) " +
				"VALUES ('" + IncidentBackOfficeAddonId + "', 6, 250, 'price_0UVD6mqJFDZJcnkVqBlHZD0w', '');");

			if (!Schema.Table("IncidentBackOfficeBillingAccounts").Exists())
			{
				Create.Table("IncidentBackOfficeBillingAccounts")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("Provider").AsString(20).NotNullable()
					.WithColumn("CustomerId").AsString(100).NotNullable()
					.WithColumn("PlanAddonId").AsString(36).NotNullable()
					.WithColumn("PriceId").AsString(100).NotNullable()
					.WithColumn("SubscriptionId").AsString(100).Nullable()
					.WithColumn("CheckoutId").AsString(100).Nullable()
					.WithColumn("CheckoutUrl").AsString(2048).Nullable()
					.WithColumn("CheckoutExpiresOn").AsDateTime2().Nullable()
					.WithColumn("CheckoutAttempt").AsString(36).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Incident.BackOffice') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally], [IsPermanent]) " +
				"VALUES ('Incident.BackOffice', 'Incident Back Office', 'Operator master toggle for the paid Incident Back Office add-on (large-incident-logistics-finance-plan.md, LI-A Foundation and LI-B Logistics: activations, periods, positions, team rosters, participants, requests, check-in, demob, facilities, lodging, feeding, services, ground support, vendors). Prerequisite of Incident.Business and Incident.Coordination; the purchase itself is the Incident Back Office add-on (PlanAddonTypes 6). Seeded off.', 'Incident', 0, 1);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Incident.Business') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Incident.Business', 'Incident Back Office business', 'LI-C incident business: procurement, budgets and funding authority, time and equipment use, cost and accruals, cost share, claims, disaster cost recovery, the evidence intake inbox and closeout. Requires Incident.BackOffice and the Incident Back Office add-on. Seeded off.', 'Incident', 0);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Incident.Coordination') " +
				"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) " +
				"VALUES ('Incident.Coordination', 'Incident coordination boards', 'LI-D emergency-management coordination: status boards, lifelines, objectives, open actions, decisions, situational layers and interagency board sharing. Requires Incident.BackOffice and the Incident Back Office add-on. Seeded off.', 'Incident', 0);");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
				"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
				"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
				"  WHERE f.[FlagKey] = 'Incident.Business' AND r.[FlagKey] = 'Incident.BackOffice') " +
				"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
				"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
				"WHERE f.[FlagKey] = 'Incident.Business' AND r.[FlagKey] = 'Incident.BackOffice';");

			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
				"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
				"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
				"  WHERE f.[FlagKey] = 'Incident.Coordination' AND r.[FlagKey] = 'Incident.BackOffice') " +
				"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
				"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
				"WHERE f.[FlagKey] = 'Incident.Coordination' AND r.[FlagKey] = 'Incident.BackOffice';");
		}

		public override void Down()
		{
			// Operator-owned rows (add-on catalog, rollout flags, prerequisite edges) are never removed on rollback;
			// the billing-account table is dropped only when empty.
			Execute.Sql("IF OBJECT_ID('[IncidentBackOfficeBillingAccounts]', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [IncidentBackOfficeBillingAccounts]) DROP TABLE [IncidentBackOfficeBillingAccounts];");
		}
	}
}
