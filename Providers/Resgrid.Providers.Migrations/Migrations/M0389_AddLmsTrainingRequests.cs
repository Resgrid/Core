using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T3 training requests with cost and approval (§13a.2; LMS add-on): LmsTrainingRequests (an outside class, its cost lines, the
	/// approval steps fixed at submission, the step waiting, the external submission written on completion) and LmsTrainingRequestDecisions
	/// (each approver's decision). No foreign keys (§17.3); UnlinkedDepartmentCleanup removes the rows. Existence-guarded for safe retry.
	/// </summary>
	[Migration(389)]
	public class M0389_AddLmsTrainingRequests : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTrainingRequests', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTrainingRequests] (
		[LmsTrainingRequestId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Title] nvarchar(250) NOT NULL,
		[Provider] nvarchar(250) NULL,
		[Location] nvarchar(250) NULL,
		[CourseCode] nvarchar(100) NULL,
		[StartsOn] datetime2 NULL,
		[EndsOn] datetime2 NULL,
		[Hours] decimal(12,2) NOT NULL CONSTRAINT [DF_LmsTrainingRequests_Hours] DEFAULT (0),
		[CategoryId] int NULL,
		[CostTuition] decimal(12,2) NOT NULL CONSTRAINT [DF_LmsTrainingRequests_CostTuition] DEFAULT (0),
		[CostTravel] decimal(12,2) NOT NULL CONSTRAINT [DF_LmsTrainingRequests_CostTravel] DEFAULT (0),
		[CostOther] decimal(12,2) NOT NULL CONSTRAINT [DF_LmsTrainingRequests_CostOther] DEFAULT (0),
		[Currency] nvarchar(3) NULL,
		[Justification] nvarchar(4000) NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsTrainingRequests_Status] DEFAULT (0),
		[CurrentStep] int NULL,
		[Steps] nvarchar(50) NULL,
		[SubmittedOn] datetime2 NOT NULL,
		[DecidedOn] datetime2 NULL,
		[DecidedByUserId] nvarchar(128) NULL,
		[LearningRecordId] int NULL,
		[CompletedOn] datetime2 NULL,
		[ActualCost] decimal(12,2) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsTrainingRequests] PRIMARY KEY CLUSTERED ([LmsTrainingRequestId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingRequests_Department_Status' AND object_id = OBJECT_ID('dbo.LmsTrainingRequests'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingRequests_Department_Status] ON [dbo].[LmsTrainingRequests] ([DepartmentId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingRequests_Department_User' AND object_id = OBJECT_ID('dbo.LmsTrainingRequests'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingRequests_Department_User] ON [dbo].[LmsTrainingRequests] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTrainingRequestDecisions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTrainingRequestDecisions] (
		[LmsTrainingRequestDecisionId] int IDENTITY(1,1) NOT NULL,
		[LmsTrainingRequestId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[Step] int NOT NULL,
		[Decision] int NOT NULL,
		[DecidedByUserId] nvarchar(128) NULL,
		[Note] nvarchar(2000) NULL,
		[DecidedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsTrainingRequestDecisions] PRIMARY KEY CLUSTERED ([LmsTrainingRequestDecisionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingRequestDecisions_Request' AND object_id = OBJECT_ID('dbo.LmsTrainingRequestDecisions'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingRequestDecisions_Request] ON [dbo].[LmsTrainingRequestDecisions] ([LmsTrainingRequestId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingRequestDecisions_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsTrainingRequestDecisions'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingRequestDecisions_DepartmentId] ON [dbo].[LmsTrainingRequestDecisions] ([DepartmentId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsTrainingRequestDecisions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTrainingRequestDecisions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsTrainingRequests', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTrainingRequests];");
		}
	}
}
