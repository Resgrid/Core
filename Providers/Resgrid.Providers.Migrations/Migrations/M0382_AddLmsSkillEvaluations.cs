using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 skill evaluations (§13 T4 item 2, §13a Evaluations+; LMS add-on): LmsSkillEvaluations (answers, timers, result, the S3
	/// request, the offline idempotency key under a filtered unique index), LmsSkillEvaluationMembers (one row per evaluated member; crew splits)
	/// and LmsSkillEvaluationFiles (photo, video and file evidence with its SHA-256).
	/// No foreign keys (§17.3: LMS tables reference baseline ids only); LmsSkillsDepartmentCleanup removes the rows. Existence-guarded for safe
	/// retry.
	/// </summary>
	[Migration(382)]
	public class M0382_AddLmsSkillEvaluations : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillEvaluations', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillEvaluations] (
		[LmsSkillEvaluationId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsSkillSheetId] int NULL,
		[LmsSkillSheetVersionId] int NULL,
		[Title] nvarchar(300) NULL,
		[Code] nvarchar(100) NULL,
		[Mode] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_Mode] DEFAULT (0),
		[Status] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_Status] DEFAULT (0),
		[Result] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_Result] DEFAULT (0),
		[Score] decimal(7,2) NULL,
		[CriticalFailed] bit NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_CriticalFailed] DEFAULT (0),
		[TimeExceeded] bit NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_TimeExceeded] DEFAULT (0),
		[EvaluatorUserId] nvarchar(128) NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[StartedOn] datetime2 NOT NULL,
		[SubmittedOn] datetime2 NULL,
		[CompletedOn] datetime2 NULL,
		[DurationSeconds] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_DurationSeconds] DEFAULT (0),
		[AnswersJson] nvarchar(max) NULL,
		[Notes] nvarchar(4000) NULL,
		[CallId] int NULL,
		[DeploymentId] int NULL,
		[Location] nvarchar(250) NULL,
		[LmsTaskBookId] int NULL,
		[TaskKey] nvarchar(40) NULL,
		[ClassSessionRef] nvarchar(100) NULL,
		[ParentEvaluationId] int NULL,
		[IdempotencyKey] nvarchar(100) NULL,
		[ContentHash] nvarchar(64) NULL,
		[SignatureRequestId] int NULL,
		[NextSignerUserId] nvarchar(128) NULL,
		[ExternalEvaluatorName] nvarchar(200) NULL,
		[VoidedOn] datetime2 NULL,
		[VoidedByUserId] nvarchar(128) NULL,
		[VoidReason] nvarchar(1000) NULL,
		[UpdatedOn] datetime2 NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsSkillEvaluations_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsSkillEvaluations] PRIMARY KEY CLUSTERED ([LmsSkillEvaluationId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluations_Department_Started' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluations'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluations_Department_Started] ON [dbo].[LmsSkillEvaluations] ([DepartmentId], [StartedOn]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluations_Evaluator' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluations'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluations_Evaluator] ON [dbo].[LmsSkillEvaluations] ([DepartmentId], [EvaluatorUserId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluations_TaskBook' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluations'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluations_TaskBook] ON [dbo].[LmsSkillEvaluations] ([DepartmentId], [LmsTaskBookId]) WHERE [LmsTaskBookId] IS NOT NULL;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluations_Request' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluations'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluations_Request] ON [dbo].[LmsSkillEvaluations] ([DepartmentId], [SignatureRequestId]) WHERE [SignatureRequestId] IS NOT NULL;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsSkillEvaluations_Idempotency' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluations'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsSkillEvaluations_Idempotency] ON [dbo].[LmsSkillEvaluations] ([DepartmentId], [IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillEvaluationMembers', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillEvaluationMembers] (
		[LmsSkillEvaluationMemberId] int IDENTITY(1,1) NOT NULL,
		[LmsSkillEvaluationId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[State] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluationMembers_State] DEFAULT (0),
		[AttemptNumber] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluationMembers_AttemptNumber] DEFAULT (1),
		[SplitToEvaluationId] int NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsSkillEvaluationMembers] PRIMARY KEY CLUSTERED ([LmsSkillEvaluationMemberId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluationMembers_Evaluation' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluationMembers'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluationMembers_Evaluation] ON [dbo].[LmsSkillEvaluationMembers] ([LmsSkillEvaluationId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluationMembers_User' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluationMembers'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluationMembers_User] ON [dbo].[LmsSkillEvaluationMembers] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillEvaluationFiles', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillEvaluationFiles] (
		[LmsSkillEvaluationFileId] int IDENTITY(1,1) NOT NULL,
		[LmsSkillEvaluationId] int NOT NULL,
		[DepartmentId] int NOT NULL,
		[StepKey] nvarchar(40) NULL,
		[FileName] nvarchar(250) NULL,
		[ContentType] nvarchar(150) NULL,
		[Size] int NOT NULL CONSTRAINT [DF_LmsSkillEvaluationFiles_Size] DEFAULT (0),
		[Sha256] nvarchar(64) NULL,
		[Data] varbinary(max) NULL,
		[UploadedByUserId] nvarchar(128) NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsSkillEvaluationFiles] PRIMARY KEY CLUSTERED ([LmsSkillEvaluationFileId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillEvaluationFiles_Evaluation' AND object_id = OBJECT_ID('dbo.LmsSkillEvaluationFiles'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillEvaluationFiles_Evaluation] ON [dbo].[LmsSkillEvaluationFiles] ([DepartmentId], [LmsSkillEvaluationId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillEvaluationFiles', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillEvaluationFiles];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillEvaluationMembers', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillEvaluationMembers];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillEvaluations', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillEvaluations];");
		}
	}
}
