using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Workforce &amp; Business Operations plan, Phase E (E2): seeds the two Phase E feature flags off with Business.Operations prerequisites — Workforce.InternalCosting and Compliance.CaliforniaPayDataReporting (the latter also fails closed without an Enabled Advanced Data Protection enrollment) — and the cross-table indexes. ADP catalog 28 is registered in code (WorkforceProtectedFields). Registry M0224. Guarded for safe retry.
	/// </summary>
	[Migration(224)]
	public class M0224_SeedWorkforceFeaturesAndIndexes : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Workforce.InternalCosting') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Workforce.InternalCosting', 'Workforce internal costing', 'Protected employee compensation profiles, resource cost profiles and internal field-cost / margin runs for bids, calls and deployments (Workforce & Business Operations plan, Phase E). Requires Business.Operations and an Advanced Data Protection enrollment. Seeded off.', 'Business', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] WHERE f.[FlagKey] = 'Workforce.InternalCosting' AND r.[FlagKey] = 'Business.Operations') INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r WHERE f.[FlagKey] = 'Workforce.InternalCosting' AND r.[FlagKey] = 'Business.Operations';");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlags] WHERE [FlagKey] = 'Compliance.CaliforniaPayDataReporting') INSERT INTO [FeatureFlags] ([FlagKey], [Name], [Description], [Category], [IsEnabledGlobally]) VALUES ('Compliance.CaliforniaPayDataReporting', 'California pay data reporting', 'California CRD (Government Code 12999) Payroll Employee and Labor Contractor Employee report preparation and export (Workforce & Business Operations plan, Phase E). Requires Business.Operations and an Enabled Advanced Data Protection enrollment; the user files through the CRD portal. Seeded off.', 'Business', 0);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [FeatureFlagPrerequisites] p JOIN [FeatureFlags] f ON f.[FeatureFlagId] = p.[FeatureFlagId] JOIN [FeatureFlags] r ON r.[FeatureFlagId] = p.[RequiredFeatureFlagId] WHERE f.[FlagKey] = 'Compliance.CaliforniaPayDataReporting' AND r.[FlagKey] = 'Business.Operations') INSERT INTO [FeatureFlagPrerequisites] ([FeatureFlagId], [RequiredFeatureFlagId], [RequiredValue]) SELECT f.[FeatureFlagId], r.[FeatureFlagId], NULL FROM [FeatureFlags] f CROSS JOIN [FeatureFlags] r WHERE f.[FlagKey] = 'Compliance.CaliforniaPayDataReporting' AND r.[FlagKey] = 'Business.Operations';");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkforceWorkEntries_Employment' AND object_id = OBJECT_ID('WorkforceWorkEntries')) CREATE INDEX [IX_WorkforceWorkEntries_Employment] ON [WorkforceWorkEntries] ([WorkforceEmploymentId], [WorkDate]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_WorkforceAnnualPayFacts_Version' AND object_id = OBJECT_ID('WorkforceAnnualPayFacts')) CREATE UNIQUE INDEX [UX_WorkforceAnnualPayFacts_Version] ON [WorkforceAnnualPayFacts] ([WorkforceEmploymentId], [ReportingYear], [ReportType], [ClientAllocationKey], [Version]) WHERE [IsDeleted] = 0;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PayDataReportingDemographics_Worker' AND object_id = OBJECT_ID('PayDataReportingDemographics')) CREATE UNIQUE INDEX [UX_PayDataReportingDemographics_Worker] ON [PayDataReportingDemographics] ([WorkforceWorkerId], [EffectiveOn]) WHERE [IsDeleted] = 0;");
		}

		public override void Down()
		{
			Execute.Sql("DELETE FROM [FeatureFlagPrerequisites] WHERE [FeatureFlagId] IN (SELECT [FeatureFlagId] FROM [FeatureFlags] WHERE [FlagKey] = 'Workforce.InternalCosting');");
			Execute.Sql("DELETE FROM [FeatureFlags] WHERE [FlagKey] = 'Workforce.InternalCosting';");
			Execute.Sql("DELETE FROM [FeatureFlagPrerequisites] WHERE [FeatureFlagId] IN (SELECT [FeatureFlagId] FROM [FeatureFlags] WHERE [FlagKey] = 'Compliance.CaliforniaPayDataReporting');");
			Execute.Sql("DELETE FROM [FeatureFlags] WHERE [FlagKey] = 'Compliance.CaliforniaPayDataReporting';");
		}
	}
}
