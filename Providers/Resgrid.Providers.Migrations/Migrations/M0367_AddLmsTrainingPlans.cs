using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 annual training plan (protocols-training-knowledge-plan §13 T3 item 3a, R-C5; LMS add-on): LmsTrainingPlans (a year,
	/// a department, group or shift audience, draft / adopted / archived) and LmsTrainingPlanItems (planned hours per member and sessions
	/// per category per month, the calendar item "make it a calendar item" created, and a reserved T4 class-session seam). No foreign
	/// keys (an LMS table references baseline ids only, §17.3); UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(367)]
	public class M0367_AddLmsTrainingPlans : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTrainingPlans', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTrainingPlans] (
		[LmsTrainingPlanId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Year] int NOT NULL,
		[Name] nvarchar(150) NOT NULL,
		[ScopeKind] int NOT NULL CONSTRAINT [DF_LmsTrainingPlans_ScopeKind] DEFAULT (0),
		[ScopeId] int NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsTrainingPlans_Status] DEFAULT (0),
		[Notes] nvarchar(4000) NULL,
		[AdoptedOn] datetime2 NULL,
		[AdoptedByUserId] nvarchar(128) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsTrainingPlans_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsTrainingPlans] PRIMARY KEY CLUSTERED ([LmsTrainingPlanId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingPlans_Department_Year' AND object_id = OBJECT_ID('dbo.LmsTrainingPlans'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingPlans_Department_Year] ON [dbo].[LmsTrainingPlans] ([DepartmentId], [Year]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTrainingPlanItems', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTrainingPlanItems] (
		[LmsTrainingPlanItemId] int IDENTITY(1,1) NOT NULL,
		[LmsTrainingPlanId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[Month] int NOT NULL,
		[CategoryId] int NULL,
		[Title] nvarchar(200) NOT NULL,
		[PlannedHours] decimal(12,2) NOT NULL CONSTRAINT [DF_LmsTrainingPlanItems_PlannedHours] DEFAULT (0),
		[PlannedSessions] int NOT NULL CONSTRAINT [DF_LmsTrainingPlanItems_PlannedSessions] DEFAULT (1),
		[PlannedOn] datetime2 NULL,
		[Notes] nvarchar(2000) NULL,
		[CalendarItemId] int NULL,
		[ClassSessionId] int NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsTrainingPlanItems] PRIMARY KEY CLUSTERED ([LmsTrainingPlanItemId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingPlanItems_Plan' AND object_id = OBJECT_ID('dbo.LmsTrainingPlanItems'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingPlanItems_Plan] ON [dbo].[LmsTrainingPlanItems] ([LmsTrainingPlanId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingPlanItems_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsTrainingPlanItems'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingPlanItems_DepartmentId] ON [dbo].[LmsTrainingPlanItems] ([DepartmentId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsTrainingPlanItems', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTrainingPlanItems];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsTrainingPlans', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTrainingPlans];");
		}
	}
}
