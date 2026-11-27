using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 lesson plans (protocols-training-knowledge-plan §13 T4 item 5, R-C6; LMS add-on) on the content core: LmsLessonPlans (the
	/// plan, the starter it came from, the published version) and LmsLessonPlanVersions (P1 lifecycle status, number, the JSON content —
	/// objectives, competency items, materials and props, ratio, safety plan, pre-brief checklist — its SHA-256, the change summary and
	/// who submitted, decided and published). No foreign keys (§17.3). Existence-guarded for safe retry.
	/// </summary>
	[Migration(379)]
	public class M0379_AddLmsLessonPlans : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLessonPlans', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLessonPlans] (
		[LmsLessonPlanId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Title] nvarchar(250) NOT NULL,
		[StarterKey] nvarchar(100) NULL,
		[CurrentVersionId] int NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsLessonPlans_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsLessonPlans] PRIMARY KEY CLUSTERED ([LmsLessonPlanId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonPlans_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsLessonPlans'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonPlans_DepartmentId] ON [dbo].[LmsLessonPlans] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLessonPlanVersions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLessonPlanVersions] (
		[LmsLessonPlanVersionId] int IDENTITY(1,1) NOT NULL,
		[LmsLessonPlanId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsLessonPlanVersions_Status] DEFAULT (0),
		[Major] int NOT NULL CONSTRAINT [DF_LmsLessonPlanVersions_Major] DEFAULT (0),
		[Minor] int NOT NULL CONSTRAINT [DF_LmsLessonPlanVersions_Minor] DEFAULT (0),
		[Title] nvarchar(250) NOT NULL,
		[ContentJson] nvarchar(max) NULL,
		[ContentHash] nvarchar(64) NULL,
		[ChangeSummary] nvarchar(1000) NULL,
		[SubmittedOn] datetime2 NULL,
		[SubmittedByUserId] nvarchar(128) NULL,
		[DecidedOn] datetime2 NULL,
		[DecidedByUserId] nvarchar(128) NULL,
		[DecisionNote] nvarchar(1000) NULL,
		[PublishedOn] datetime2 NULL,
		[PublishedByUserId] nvarchar(128) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsLessonPlanVersions] PRIMARY KEY CLUSTERED ([LmsLessonPlanVersionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonPlanVersions_Plan' AND object_id = OBJECT_ID('dbo.LmsLessonPlanVersions'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonPlanVersions_Plan] ON [dbo].[LmsLessonPlanVersions] ([DepartmentId], [LmsLessonPlanId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessonPlanVersions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLessonPlanVersions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessonPlans', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLessonPlans];");
		}
	}
}
