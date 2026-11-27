using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9A: validity rules per certification type. DepartmentCertificationTypes.ValidityRule (0 RollingMonths = the behaviour before,
	/// 1 CalendarYearEnd, 2 FiscalYearEnd, 3 FixedDate), the fiscal year end (ValidityYearEndMonth / ValidityYearEndDay), the fixed date
	/// (ValidityFixedDate), the renewal grace (RenewalGraceDays) and the renewal window (RenewalWindowMonths). Baseline (free). Existing types
	/// read as rolling months, unchanged. Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(404)]
	public class M0404_AddCertificationValidityRules : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityRule') IS NULL
	ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [ValidityRule] int NOT NULL CONSTRAINT [DF_DepartmentCertificationTypes_ValidityRule] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityYearEndMonth') IS NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [ValidityYearEndMonth] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityYearEndDay') IS NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [ValidityYearEndDay] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityFixedDate') IS NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [ValidityFixedDate] datetime2 NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'RenewalGraceDays') IS NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [RenewalGraceDays] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.DepartmentCertificationTypes', 'U') IS NOT NULL AND COL_LENGTH('dbo.DepartmentCertificationTypes', 'RenewalWindowMonths') IS NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] ADD [RenewalWindowMonths] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'RenewalWindowMonths') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [RenewalWindowMonths];");
			Execute.Sql("IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'RenewalGraceDays') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [RenewalGraceDays];");
			Execute.Sql("IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityFixedDate') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [ValidityFixedDate];");
			Execute.Sql("IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityYearEndDay') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [ValidityYearEndDay];");
			Execute.Sql("IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityYearEndMonth') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [ValidityYearEndMonth];");
			Execute.Sql(@"
IF OBJECT_ID('DF_DepartmentCertificationTypes_ValidityRule', 'D') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP CONSTRAINT [DF_DepartmentCertificationTypes_ValidityRule];
IF COL_LENGTH('dbo.DepartmentCertificationTypes', 'ValidityRule') IS NOT NULL ALTER TABLE [dbo].[DepartmentCertificationTypes] DROP COLUMN [ValidityRule];");
		}
	}
}
