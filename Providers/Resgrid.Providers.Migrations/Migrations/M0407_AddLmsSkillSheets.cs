using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 skill sheets (plan §13 T4 item 2; LMS add-on): LmsSkillSheets (the identity, who may evaluate and sign next, self and crew
	/// evaluation) and LmsSkillSheetVersions (the steps as JSON on the content core's version shape, the grading settings and the content hash).
	/// No foreign keys (§17.3: LMS tables reference baseline ids only); LmsSkillsDepartmentCleanup removes the rows. Existence-guarded for safe
	/// retry.
	/// </summary>
	[Migration(407)]
	public class M0407_AddLmsSkillSheets : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillSheets', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillSheets] (
		[LmsSkillSheetId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Code] nvarchar(100) NULL,
		[Title] nvarchar(250) NOT NULL,
		[Description] nvarchar(4000) NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsSkillSheets_Kind] DEFAULT (0),
		[Status] int NOT NULL CONSTRAINT [DF_LmsSkillSheets_Status] DEFAULT (0),
		[CurrentVersionId] int NULL,
		[CategoryId] int NULL,
		[EvaluatorEligibilityJson] nvarchar(2000) NULL,
		[NextSignerEligibilityJson] nvarchar(2000) NULL,
		[AllowSelfEvaluation] bit NOT NULL CONSTRAINT [DF_LmsSkillSheets_AllowSelfEvaluation] DEFAULT (0),
		[AllowCrewEvaluation] bit NOT NULL CONSTRAINT [DF_LmsSkillSheets_AllowCrewEvaluation] DEFAULT (0),
		[TemplateKey] nvarchar(100) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsSkillSheets_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsSkillSheets_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsSkillSheets] PRIMARY KEY CLUSTERED ([LmsSkillSheetId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillSheets_Department' AND object_id = OBJECT_ID('dbo.LmsSkillSheets'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillSheets_Department] ON [dbo].[LmsSkillSheets] ([DepartmentId], [IsDeleted]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillSheetVersions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillSheetVersions] (
		[LmsSkillSheetVersionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsSkillSheetId] int NOT NULL,
		[Major] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_Major] DEFAULT (0),
		[Minor] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_Minor] DEFAULT (0),
		[Status] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_Status] DEFAULT (0),
		[ContentJson] nvarchar(max) NULL,
		[ContentHash] nvarchar(64) NULL,
		[ChangeSummary] nvarchar(2000) NULL,
		[PassThreshold] decimal(5,2) NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_PassThreshold] DEFAULT (100),
		[MaxAttempts] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_MaxAttempts] DEFAULT (0),
		[AttemptWindowDays] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_AttemptWindowDays] DEFAULT (0),
		[TimeLimitSeconds] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_TimeLimitSeconds] DEFAULT (0),
		[DefaultMinutes] int NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_DefaultMinutes] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[PublishedOn] datetime2 NULL,
		[PublishedByUserId] nvarchar(128) NULL,
		[SupersededOn] datetime2 NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsSkillSheetVersions_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsSkillSheetVersions] PRIMARY KEY CLUSTERED ([LmsSkillSheetVersionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillSheetVersions_Sheet' AND object_id = OBJECT_ID('dbo.LmsSkillSheetVersions'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillSheetVersions_Sheet] ON [dbo].[LmsSkillSheetVersions] ([DepartmentId], [LmsSkillSheetId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillSheetVersions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillSheetVersions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillSheets', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillSheets];");
		}
	}
}
