using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 position task books (§13 T4 item 3, NWCG model; LMS add-on): LmsTaskBookDefinitions (position code, tasks as JSON, evaluator,
	/// final evaluator and certifying rules, validity) and LmsTaskBooks (a member's book: tasks snapshotted, validity, the certification request).
	/// No foreign keys (§17.3: LMS tables reference baseline ids only); LmsSkillsDepartmentCleanup removes the rows. Existence-guarded for safe
	/// retry.
	/// </summary>
	[Migration(409)]
	public class M0409_AddLmsTaskBooks : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTaskBookDefinitions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTaskBookDefinitions] (
		[LmsTaskBookDefinitionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[PositionCode] nvarchar(40) NOT NULL,
		[Name] nvarchar(250) NOT NULL,
		[Description] nvarchar(4000) NULL,
		[Family] int NOT NULL CONSTRAINT [DF_LmsTaskBookDefinitions_Family] DEFAULT (0),
		[TasksJson] nvarchar(max) NULL,
		[CertificationTypeId] int NULL,
		[ValidityMonths] int NOT NULL CONSTRAINT [DF_LmsTaskBookDefinitions_ValidityMonths] DEFAULT (36),
		[RecertifyMonths] int NOT NULL CONSTRAINT [DF_LmsTaskBookDefinitions_RecertifyMonths] DEFAULT (0),
		[EvaluatorEligibilityJson] nvarchar(2000) NULL,
		[FinalEvaluatorEligibilityJson] nvarchar(2000) NULL,
		[CertifyingEligibilityJson] nvarchar(2000) NULL,
		[TemplateKey] nvarchar(100) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsTaskBookDefinitions_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsTaskBookDefinitions_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsTaskBookDefinitions] PRIMARY KEY CLUSTERED ([LmsTaskBookDefinitionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTaskBookDefinitions_Department' AND object_id = OBJECT_ID('dbo.LmsTaskBookDefinitions'))
	CREATE NONCLUSTERED INDEX [IX_LmsTaskBookDefinitions_Department] ON [dbo].[LmsTaskBookDefinitions] ([DepartmentId], [IsDeleted]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTaskBooks', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTaskBooks] (
		[LmsTaskBookId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsTaskBookDefinitionId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[PositionCode] nvarchar(40) NOT NULL,
		[Name] nvarchar(250) NOT NULL,
		[Family] int NOT NULL CONSTRAINT [DF_LmsTaskBooks_Family] DEFAULT (0),
		[TasksJson] nvarchar(max) NULL,
		[EvaluatorEligibilityJson] nvarchar(2000) NULL,
		[FinalEvaluatorEligibilityJson] nvarchar(2000) NULL,
		[CertifyingEligibilityJson] nvarchar(2000) NULL,
		[CertificationTypeId] int NULL,
		[PersonnelCertificationId] int NULL,
		[RecertifyMonths] int NOT NULL CONSTRAINT [DF_LmsTaskBooks_RecertifyMonths] DEFAULT (0),
		[InitiatedOn] datetime2 NOT NULL,
		[InitiatedByUserId] nvarchar(128) NULL,
		[ExpiresOn] datetime2 NOT NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsTaskBooks_Status] DEFAULT (0),
		[SignatureRequestId] int NULL,
		[FinalEvaluatorUserId] nvarchar(128) NULL,
		[CertifyingOfficialUserId] nvarchar(128) NULL,
		[RecommendedOn] datetime2 NULL,
		[CertifiedOn] datetime2 NULL,
		[CertifiedByUserId] nvarchar(128) NULL,
		[ClosedOn] datetime2 NULL,
		[CloseReason] nvarchar(1000) NULL,
		[UpdatedOn] datetime2 NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsTaskBooks_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsTaskBooks] PRIMARY KEY CLUSTERED ([LmsTaskBookId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTaskBooks_Department_User' AND object_id = OBJECT_ID('dbo.LmsTaskBooks'))
	CREATE NONCLUSTERED INDEX [IX_LmsTaskBooks_Department_User] ON [dbo].[LmsTaskBooks] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTaskBooks_Status_Expires' AND object_id = OBJECT_ID('dbo.LmsTaskBooks'))
	CREATE NONCLUSTERED INDEX [IX_LmsTaskBooks_Status_Expires] ON [dbo].[LmsTaskBooks] ([Status], [ExpiresOn]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsTaskBooks', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTaskBooks];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsTaskBookDefinitions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTaskBookDefinitions];");
		}
	}
}
