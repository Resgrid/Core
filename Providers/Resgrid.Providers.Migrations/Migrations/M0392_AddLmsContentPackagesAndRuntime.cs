using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 standards (round 6C): SCORM / cmi5 content packages (LmsContentPackages, unpacked by worker 98), the per-enrollment runtime
	/// record of a standards lesson (LmsRuntimeRegistrations, unique per enrollment and lesson), launch sessions bound to hashed bearer tokens
	/// (LmsRuntimeSessions), and the cmi5 minimal LRS (LmsCmi5Statements unique per registration and statement id, LmsCmi5StateDocuments).
	/// No foreign keys (§17.3: LMS tables reference baseline ids only); LmsStandardsDepartmentCleanup removes the rows. A state document's
	/// activity and state ids are IRIs compared exactly, so they carry a binary collation (the database default is case-insensitive).
	/// Existence-guarded for safe retry; Down drops the tables, children first.
	/// </summary>
	[Migration(392)]
	public class M0392_AddLmsContentPackagesAndRuntime : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsContentPackages', 'U') IS NULL
CREATE TABLE [dbo].[LmsContentPackages] (
	[LmsContentPackageId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsContentPackages] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[Standard] int NOT NULL CONSTRAINT [DF_LmsContentPackages_Standard] DEFAULT 0,
	[Title] nvarchar(250) NULL,
	[Identifier] nvarchar(500) NULL,
	[SchemaVersion] nvarchar(50) NULL,
	[FileName] nvarchar(260) NULL,
	[Sha256] varchar(64) NULL,
	[Status] int NOT NULL CONSTRAINT [DF_LmsContentPackages_Status] DEFAULT 0,
	[SourceKey] nvarchar(1000) NULL,
	[SourceBytes] bigint NOT NULL CONSTRAINT [DF_LmsContentPackages_SourceBytes] DEFAULT 0,
	[ContentPrefix] nvarchar(1000) NULL,
	[FileCount] int NOT NULL CONSTRAINT [DF_LmsContentPackages_FileCount] DEFAULT 0,
	[ContentBytes] bigint NOT NULL CONSTRAINT [DF_LmsContentPackages_ContentBytes] DEFAULT 0,
	[ManifestJson] nvarchar(max) NULL,
	[Attempts] int NOT NULL CONSTRAINT [DF_LmsContentPackages_Attempts] DEFAULT 0,
	[LastError] nvarchar(2000) NULL,
	[ClaimedOn] datetime2 NULL,
	[CreatedOn] datetime2 NOT NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[ReadyOn] datetime2 NULL,
	[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsContentPackages_IsDeleted] DEFAULT 0,
	[DeletedOn] datetime2 NULL,
	[DeletedByUserId] nvarchar(128) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsContentPackages_Department' AND object_id = OBJECT_ID('dbo.LmsContentPackages')) " +
				"CREATE INDEX [IX_LmsContentPackages_Department] ON [dbo].[LmsContentPackages] ([DepartmentId], [IsDeleted]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsContentPackages_Queue' AND object_id = OBJECT_ID('dbo.LmsContentPackages')) " +
				"CREATE INDEX [IX_LmsContentPackages_Queue] ON [dbo].[LmsContentPackages] ([Status], [CreatedOn]) WHERE [Status] IN (0, 1);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRuntimeRegistrations', 'U') IS NULL
CREATE TABLE [dbo].[LmsRuntimeRegistrations] (
	[LmsRuntimeRegistrationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsRuntimeRegistrations] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[LmsEnrollmentId] int NOT NULL,
	[LmsLessonId] int NOT NULL,
	[UserId] nvarchar(128) NOT NULL,
	[Kind] int NOT NULL,
	[LmsContentPackageId] int NULL,
	[LmsLtiToolId] int NULL,
	[RegistrationKey] nvarchar(100) NULL,
	[StateJson] nvarchar(max) NULL,
	[CompletionStatus] nvarchar(32) NULL,
	[SuccessStatus] nvarchar(32) NULL,
	[ScorePercent] decimal(9,2) NULL,
	[TotalSeconds] int NOT NULL CONSTRAINT [DF_LmsRuntimeRegistrations_TotalSeconds] DEFAULT 0,
	[LaunchCount] int NOT NULL CONSTRAINT [DF_LmsRuntimeRegistrations_LaunchCount] DEFAULT 0,
	[Satisfied] bit NOT NULL CONSTRAINT [DF_LmsRuntimeRegistrations_Satisfied] DEFAULT 0,
	[SatisfiedOn] datetime2 NULL,
	[LastResultOn] datetime2 NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedOn] datetime2 NULL,
	[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsRuntimeRegistrations_RowVersion] DEFAULT 0
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRuntimeRegistrations_Enrollment_Lesson' AND object_id = OBJECT_ID('dbo.LmsRuntimeRegistrations')) " +
				"CREATE UNIQUE INDEX [UX_LmsRuntimeRegistrations_Enrollment_Lesson] ON [dbo].[LmsRuntimeRegistrations] ([DepartmentId], [LmsEnrollmentId], [LmsLessonId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRuntimeRegistrations_Lesson_Key' AND object_id = OBJECT_ID('dbo.LmsRuntimeRegistrations')) " +
				"CREATE INDEX [IX_LmsRuntimeRegistrations_Lesson_Key] ON [dbo].[LmsRuntimeRegistrations] ([DepartmentId], [LmsLessonId], [RegistrationKey]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsRuntimeSessions', 'U') IS NULL
CREATE TABLE [dbo].[LmsRuntimeSessions] (
	[LmsRuntimeSessionId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsRuntimeSessions] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[LmsRuntimeRegistrationId] int NOT NULL,
	[LmsEnrollmentId] int NOT NULL,
	[LmsLessonId] int NOT NULL,
	[UserId] nvarchar(128) NOT NULL,
	[Kind] int NOT NULL,
	[ItemIdentifier] nvarchar(500) NULL,
	[SessionKey] nvarchar(64) NULL,
	[TokenHash] varchar(64) NOT NULL,
	[FetchTokenHash] varchar(64) NULL,
	[AuthTokenHash] varchar(64) NULL,
	[FetchedOn] datetime2 NULL,
	[CreatedOn] datetime2 NOT NULL,
	[ExpiresOn] datetime2 NOT NULL,
	[UsedOn] datetime2 NULL,
	[EndedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRuntimeSessions_Token' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')) " +
				"CREATE UNIQUE INDEX [UX_LmsRuntimeSessions_Token] ON [dbo].[LmsRuntimeSessions] ([TokenHash]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRuntimeSessions_Fetch' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')) " +
				"CREATE UNIQUE INDEX [UX_LmsRuntimeSessions_Fetch] ON [dbo].[LmsRuntimeSessions] ([FetchTokenHash]) WHERE [FetchTokenHash] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsRuntimeSessions_Auth' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')) " +
				"CREATE UNIQUE INDEX [UX_LmsRuntimeSessions_Auth] ON [dbo].[LmsRuntimeSessions] ([AuthTokenHash]) WHERE [AuthTokenHash] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRuntimeSessions_Expires' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')) " +
				"CREATE INDEX [IX_LmsRuntimeSessions_Expires] ON [dbo].[LmsRuntimeSessions] ([ExpiresOn]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsRuntimeSessions_Department' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')) " +
				"CREATE INDEX [IX_LmsRuntimeSessions_Department] ON [dbo].[LmsRuntimeSessions] ([DepartmentId], [LmsRuntimeRegistrationId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCmi5Statements', 'U') IS NULL
CREATE TABLE [dbo].[LmsCmi5Statements] (
	[LmsCmi5StatementId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsCmi5Statements] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[LmsRuntimeRegistrationId] int NOT NULL,
	[LmsRuntimeSessionId] int NOT NULL,
	[StatementId] varchar(36) NOT NULL,
	[VerbId] nvarchar(250) NULL,
	[StatementJson] nvarchar(max) NULL,
	[StoredOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCmi5Statements_Registration_Statement' AND object_id = OBJECT_ID('dbo.LmsCmi5Statements')) " +
				"CREATE UNIQUE INDEX [UX_LmsCmi5Statements_Registration_Statement] ON [dbo].[LmsCmi5Statements] ([LmsRuntimeRegistrationId], [StatementId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCmi5Statements_Department' AND object_id = OBJECT_ID('dbo.LmsCmi5Statements')) " +
				"CREATE INDEX [IX_LmsCmi5Statements_Department] ON [dbo].[LmsCmi5Statements] ([DepartmentId], [LmsRuntimeRegistrationId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCmi5StateDocuments', 'U') IS NULL
CREATE TABLE [dbo].[LmsCmi5StateDocuments] (
	[LmsCmi5StateDocumentId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LmsCmi5StateDocuments] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[LmsRuntimeRegistrationId] int NOT NULL,
	[ActivityId] nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
	[StateId] nvarchar(250) COLLATE Latin1_General_100_BIN2 NOT NULL,
	[ContentType] nvarchar(100) NULL,
	[Content] nvarchar(max) NULL,
	[UpdatedOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCmi5StateDocuments_Key' AND object_id = OBJECT_ID('dbo.LmsCmi5StateDocuments')) " +
				"CREATE UNIQUE INDEX [UX_LmsCmi5StateDocuments_Key] ON [dbo].[LmsCmi5StateDocuments] ([LmsRuntimeRegistrationId], [StateId], [ActivityId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCmi5StateDocuments_Department' AND object_id = OBJECT_ID('dbo.LmsCmi5StateDocuments')) " +
				"CREATE INDEX [IX_LmsCmi5StateDocuments_Department] ON [dbo].[LmsCmi5StateDocuments] ([DepartmentId]);");
		}

		public override void Down()
		{
			foreach (var table in new[] { "LmsCmi5StateDocuments", "LmsCmi5Statements", "LmsRuntimeSessions", "LmsRuntimeRegistrations", "LmsContentPackages" })
				Execute.Sql($"IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL DROP TABLE [dbo].[{table}];");
		}
	}
}
