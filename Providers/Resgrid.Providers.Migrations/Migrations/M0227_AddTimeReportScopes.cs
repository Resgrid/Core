using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Crew and individual time reports (mobile field time accounting). A daily time report gains an optional scope: the
	/// deployed unit whose Crew Time Report it is, or the single roster row it covers. The one-report-per-day rule becomes
	/// one report per day per scope; SQL Server treats NULLs as equal in a unique index, so the deployment-wide report stays
	/// one per day.
	/// </summary>
	[Migration(227)]
	public class M0227_AddTimeReportScopes : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('DeploymentTimeReports', 'DeploymentUnitId') IS NULL ALTER TABLE [DeploymentTimeReports] ADD [DeploymentUnitId] nvarchar(36) NULL;");
			Execute.Sql("IF COL_LENGTH('DeploymentTimeReports', 'DeploymentPersonnelId') IS NULL ALTER TABLE [DeploymentTimeReports] ADD [DeploymentPersonnelId] nvarchar(36) NULL;");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Date' AND object_id = OBJECT_ID('DeploymentTimeReports')) DROP INDEX [UX_DeploymentTimeReports_Date] ON [DeploymentTimeReports];");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Scope' AND object_id = OBJECT_ID('DeploymentTimeReports')) CREATE UNIQUE INDEX [UX_DeploymentTimeReports_Scope] ON [DeploymentTimeReports] ([DeploymentId], [ReportDate], [DeploymentUnitId], [DeploymentPersonnelId]) WHERE [IsDeleted] = 0 AND [Status] <> 4;");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM [DeploymentTimeReports] WHERE [DeploymentUnitId] IS NOT NULL OR [DeploymentPersonnelId] IS NOT NULL) THROW 51000, 'Crew and individual time reports exist; rolling back would merge them into one report per day.', 1;");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Scope' AND object_id = OBJECT_ID('DeploymentTimeReports')) DROP INDEX [UX_DeploymentTimeReports_Scope] ON [DeploymentTimeReports];");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeploymentTimeReports_Date' AND object_id = OBJECT_ID('DeploymentTimeReports')) CREATE UNIQUE INDEX [UX_DeploymentTimeReports_Date] ON [DeploymentTimeReports] ([DeploymentId], [ReportDate]) WHERE [IsDeleted] = 0 AND [Status] <> 4;");
			Delete.Column("DeploymentPersonnelId").FromTable("DeploymentTimeReports");
			Delete.Column("DeploymentUnitId").FromTable("DeploymentTimeReports");
		}
	}
}
