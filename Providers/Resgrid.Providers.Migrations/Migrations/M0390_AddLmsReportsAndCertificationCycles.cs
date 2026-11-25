using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 report catalog and certification cycles (§13a.1, §13a.3; LMS add-on): LmsReportFolders, LmsReportDefinitions (a catalog
	/// report with parameters, a folder, a daily / weekly / monthly schedule at a department-local hour, recipients; a filtered index serves
	/// worker 94's due-work query), LmsReportRuns (the run history), LmsCertificationCycleRules (auto-renew and alert recipients per
	/// certification type, unique per type), LmsCertificationAlerts (sent alerts and auto-renew claims, unique per record, kind and key) and
	/// LmsProgramStates (worker 94's per-department sweep day and familiarization watermark). No foreign keys (§17.3);
	/// UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(390)]
	public class M0390_AddLmsReportsAndCertificationCycles : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsReportFolders', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsReportFolders] (
		[LmsReportFolderId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Name] nvarchar(100) NOT NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsReportFolders_SortOrder] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsReportFolders] PRIMARY KEY CLUSTERED ([LmsReportFolderId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsReportFolders_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsReportFolders'))
	CREATE NONCLUSTERED INDEX [IX_LmsReportFolders_DepartmentId] ON [dbo].[LmsReportFolders] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsReportDefinitions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsReportDefinitions] (
		[LmsReportDefinitionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsReportFolderId] int NULL,
		[ReportKey] nvarchar(100) NOT NULL,
		[Name] nvarchar(150) NOT NULL,
		[ParametersJson] nvarchar(4000) NULL,
		[Format] int NOT NULL CONSTRAINT [DF_LmsReportDefinitions_Format] DEFAULT (0),
		[Frequency] int NOT NULL CONSTRAINT [DF_LmsReportDefinitions_Frequency] DEFAULT (0),
		[DayOfWeek] int NOT NULL CONSTRAINT [DF_LmsReportDefinitions_DayOfWeek] DEFAULT (0),
		[DayOfMonth] int NOT NULL CONSTRAINT [DF_LmsReportDefinitions_DayOfMonth] DEFAULT (1),
		[HourLocal] int NOT NULL CONSTRAINT [DF_LmsReportDefinitions_HourLocal] DEFAULT (6),
		[RecipientUserIds] nvarchar(4000) NULL,
		[NextRunUtc] datetime2 NULL,
		[LastRunUtc] datetime2 NULL,
		[LastRunStatus] int NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsReportDefinitions_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsReportDefinitions] PRIMARY KEY CLUSTERED ([LmsReportDefinitionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsReportDefinitions_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsReportDefinitions'))
	CREATE NONCLUSTERED INDEX [IX_LmsReportDefinitions_DepartmentId] ON [dbo].[LmsReportDefinitions] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsReportDefinitions_Due' AND object_id = OBJECT_ID('dbo.LmsReportDefinitions'))
	CREATE NONCLUSTERED INDEX [IX_LmsReportDefinitions_Due] ON [dbo].[LmsReportDefinitions] ([NextRunUtc]) WHERE [IsDeleted] = 0 AND [Frequency] > 0 AND [NextRunUtc] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsReportRuns', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsReportRuns] (
		[LmsReportRunId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsReportDefinitionId] int NOT NULL,
		[RanOn] datetime2 NOT NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsReportRuns_Status] DEFAULT (0),
		[Recipients] int NOT NULL CONSTRAINT [DF_LmsReportRuns_Recipients] DEFAULT (0),
		[RowTotal] int NOT NULL CONSTRAINT [DF_LmsReportRuns_RowTotal] DEFAULT (0),
		[Error] nvarchar(500) NULL,
		CONSTRAINT [PK_LmsReportRuns] PRIMARY KEY CLUSTERED ([LmsReportRunId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsReportRuns_Definition' AND object_id = OBJECT_ID('dbo.LmsReportRuns'))
	CREATE NONCLUSTERED INDEX [IX_LmsReportRuns_Definition] ON [dbo].[LmsReportRuns] ([LmsReportDefinitionId], [RanOn]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsReportRuns_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsReportRuns'))
	CREATE NONCLUSTERED INDEX [IX_LmsReportRuns_DepartmentId] ON [dbo].[LmsReportRuns] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCertificationCycleRules', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCertificationCycleRules] (
		[LmsCertificationCycleRuleId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DepartmentCertificationTypeId] int NOT NULL,
		[AutoRenewMode] int NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_AutoRenewMode] DEFAULT (0),
		[AlertOnCompletion] bit NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_AlertOnCompletion] DEFAULT (0),
		[AlertOnRenewal] bit NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_AlertOnRenewal] DEFAULT (0),
		[AlertLeadDaysCsv] nvarchar(100) NULL,
		[AlertOnExpired] bit NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_AlertOnExpired] DEFAULT (0),
		[NotifySupervisors] bit NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_NotifySupervisors] DEFAULT (0),
		[NotifyAdmins] bit NOT NULL CONSTRAINT [DF_LmsCertificationCycleRules_NotifyAdmins] DEFAULT (0),
		[NamedRecipientUserIds] nvarchar(4000) NULL,
		[AlertsFrom] datetime2 NOT NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCertificationCycleRules] PRIMARY KEY CLUSTERED ([LmsCertificationCycleRuleId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCertificationCycleRules_Type' AND object_id = OBJECT_ID('dbo.LmsCertificationCycleRules'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCertificationCycleRules_Type] ON [dbo].[LmsCertificationCycleRules] ([DepartmentId], [DepartmentCertificationTypeId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCertificationAlerts', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCertificationAlerts] (
		[LmsCertificationAlertId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[PersonnelCertificationId] int NOT NULL,
		[Kind] int NOT NULL,
		[AlertKey] nvarchar(40) NOT NULL,
		[SentOn] datetime2 NOT NULL,
		[Recipients] int NOT NULL CONSTRAINT [DF_LmsCertificationAlerts_Recipients] DEFAULT (0),
		CONSTRAINT [PK_LmsCertificationAlerts] PRIMARY KEY CLUSTERED ([LmsCertificationAlertId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCertificationAlerts_Record_Kind_Key' AND object_id = OBJECT_ID('dbo.LmsCertificationAlerts'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCertificationAlerts_Record_Kind_Key] ON [dbo].[LmsCertificationAlerts] ([PersonnelCertificationId], [Kind], [AlertKey]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCertificationAlerts_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCertificationAlerts'))
	CREATE NONCLUSTERED INDEX [IX_LmsCertificationAlerts_DepartmentId] ON [dbo].[LmsCertificationAlerts] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsProgramStates', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsProgramStates] (
		[DepartmentId] int NOT NULL,
		[CycleSweepLocalDate] datetime2 NULL,
		[FamiliarizationScannedThrough] datetime2 NULL,
		[UpdatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsProgramStates] PRIMARY KEY CLUSTERED ([DepartmentId] ASC)
	);
END");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsProgramStates', 'U') IS NOT NULL DROP TABLE [dbo].[LmsProgramStates];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCertificationAlerts', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCertificationAlerts];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCertificationCycleRules', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCertificationCycleRules];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsReportRuns', 'U') IS NOT NULL DROP TABLE [dbo].[LmsReportRuns];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsReportDefinitions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsReportDefinitions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsReportFolders', 'U') IS NOT NULL DROP TABLE [dbo].[LmsReportFolders];");
		}
	}
}
