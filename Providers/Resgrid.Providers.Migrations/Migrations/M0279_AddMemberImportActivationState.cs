using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department Import dormancy and rollout flags (wizard plan sections 8.6, 15 and 4.3). DepartmentMembers gains
	/// ImportActivationState (null = not imported or active; 1 Dormant, 2 Invited, 3 Activated): a member created by an
	/// import cannot sign in and receives nothing until an administrator invites them. Seeds the master flag
	/// Onboarding.MigrationWizard and its children off; Onboarding.ManagedMigration is operator-only and permanent, set per
	/// department by BackOffice when a managed-migration engagement is agreed.
	/// </summary>
	[Migration(279)]
	public class M0279_AddMemberImportActivationState : Migration
	{
		private const string MasterFlag = "Onboarding.MigrationWizard";

		private static readonly (string Key, string Name, string Description, bool Permanent, string[] Requires)[] Flags =
		{
			(MasterFlag, "Import & Migration", "Department Import & Migration workspace (self-service import of configuration, people and apparatus). Master switch; seeded off.", false, new string[0]),
			("Onboarding.OperationsData", "Import: operations data", "Import & Migration wave 2B: dispatch configuration, shifts, calendar, geography, contacts and Records prevention data. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.ReadinessData", "Import: readiness and business data", "Import & Migration wave 2C: inventory, checklists, maintenance plans, open work orders and Business Ops profiles. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.OperationalHistory", "Import: operational history", "Import & Migration Phase 3: closed calls, training results and certification history, excluded from reports by default. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.HistoricalRecords", "Import: historical Records", "Import & Migration Phase 3R: third-party incident history into Records as ImportedHistorical. Requires Onboarding.MigrationWizard and Records.System. Seeded off.", false, new[] { MasterFlag, "Records.System" }),
			("Onboarding.VendorPacks", "Import: vendor packs", "Import & Migration Phase 3B: sample-validated vendor export packs. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.DepartmentPackage", "Import: department package", "Import & Migration Phase 4: Resgrid department package export, cluster moves and configuration cloning. Requires Onboarding.MigrationWizard. Seeded off.", false, new[] { MasterFlag }),
			("Onboarding.ManagedMigration", "Import: managed migration", "Operator-only: enables the Operate support grant (Resgrid staff run an import end to end with two-person control) for a department with an agreed engagement. Never enabled globally; set per department by BackOffice. Requires Onboarding.MigrationWizard.", true, new[] { MasterFlag })
		};

		public override void Up()
		{
			if (!Schema.Table("DepartmentMembers").Column("ImportActivationState").Exists())
				Alter.Table("DepartmentMembers").AddColumn("ImportActivationState").AsInt32().Nullable();

			foreach (var flag in Flags)
			{
				Execute.Sql(
					"IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = '" + flag.Key + "') " +
					"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally], [IsPermanent]) " +
					"VALUES ('" + flag.Key + "', '" + flag.Name + "', '" + flag.Description + "', 'Onboarding', 0, " + (flag.Permanent ? "1" : "0") + ");");

				// Prerequisite edges: each child requires its parents to be on (RequiredValue null = enabled).
				foreach (var required in flag.Requires)
				{
					Execute.Sql(
						"IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p " +
						"  JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] " +
						"  JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] " +
						"  WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = '" + required + "') " +
						"INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) " +
						"SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r " +
						"WHERE f.[FlagKey] = '" + flag.Key + "' AND r.[FlagKey] = '" + required + "';");
				}
			}
		}

		public override void Down()
		{
			// Rollout flags and their prerequisite edges are operator-owned and never removed on rollback.
			if (Schema.Table("DepartmentMembers").Column("ImportActivationState").Exists())
				Delete.Column("ImportActivationState").FromTable("DepartmentMembers");
		}
	}
}
