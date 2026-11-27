using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T2 learner side and A1: LmsEnrollments (pinned to the version taken, R-L12; one open enrollment per member and course
	/// through a filtered unique index), LmsLessonFiles (file-request uploads), LmsCrewViewings and LmsCrewViewingAttendees (R-C4),
	/// LmsLessonProgress (resume, video position, watched percent, seat time, R-C8), LmsAssessmentAttempts (R-T7: separate from the
	/// baseline TrainingQuizAttempts; revision hash and snapshot, R-T6), LmsSurveyResponses (anonymous), LmsPathAssignments (Academy
	/// role paths) and LmsDepartmentStates (the lapse clock). Guarded for safe retry.
	/// </summary>
	[Migration(350)]
	public class M0350_AddLmsLearnerTables : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsEnrollments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsEnrollments] (
		[LmsEnrollmentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Source] int NOT NULL CONSTRAINT [DF_LmsEnrollments_Source] DEFAULT (0),
		[RolePathKey] nvarchar(64) NULL,
		[AssignedByUserId] nvarchar(128) NULL,
		[AssignedOn] datetime2 NOT NULL,
		[DueOn] datetime2 NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsEnrollments_Status] DEFAULT (0),
		[IsOpen] bit NOT NULL CONSTRAINT [DF_LmsEnrollments_IsOpen] DEFAULT (1),
		[StartedOn] datetime2 NULL,
		[CompletedOn] datetime2 NULL,
		[TestedOut] bit NOT NULL CONSTRAINT [DF_LmsEnrollments_TestedOut] DEFAULT (0),
		[CurrentLessonId] int NULL,
		[ProgressPercent] int NOT NULL CONSTRAINT [DF_LmsEnrollments_ProgressPercent] DEFAULT (0),
		[SeatSeconds] int NOT NULL CONSTRAINT [DF_LmsEnrollments_SeatSeconds] DEFAULT (0),
		[LastActivityOn] datetime2 NULL,
		[LearningRecordId] int NULL,
		[PersonnelCertificationId] int NULL,
		[ReminderSentOn] datetime2 NULL,
		[WithdrawnOn] datetime2 NULL,
		[WithdrawnByUserId] nvarchar(128) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsEnrollments_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsEnrollments] PRIMARY KEY CLUSTERED ([LmsEnrollmentId] ASC),
		CONSTRAINT [FK_LmsEnrollments_LmsCourseId] FOREIGN KEY ([LmsCourseId]) REFERENCES [dbo].[LmsCourses] ([LmsCourseId]),
		CONSTRAINT [FK_LmsEnrollments_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsEnrollments_Open' AND object_id = OBJECT_ID('dbo.LmsEnrollments'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsEnrollments_Open] ON [dbo].[LmsEnrollments] ([DepartmentId], [UserId], [LmsCourseId]) WHERE [IsOpen] = 1;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsEnrollments_Department_User' AND object_id = OBJECT_ID('dbo.LmsEnrollments'))
	CREATE NONCLUSTERED INDEX [IX_LmsEnrollments_Department_User] ON [dbo].[LmsEnrollments] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsEnrollments_Course' AND object_id = OBJECT_ID('dbo.LmsEnrollments'))
	CREATE NONCLUSTERED INDEX [IX_LmsEnrollments_Course] ON [dbo].[LmsEnrollments] ([LmsCourseId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsEnrollments_Version_User' AND object_id = OBJECT_ID('dbo.LmsEnrollments'))
	CREATE NONCLUSTERED INDEX [IX_LmsEnrollments_Version_User] ON [dbo].[LmsEnrollments] ([LmsCourseVersionId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsEnrollments_Status_LastActivityOn' AND object_id = OBJECT_ID('dbo.LmsEnrollments'))
	CREATE NONCLUSTERED INDEX [IX_LmsEnrollments_Status_LastActivityOn] ON [dbo].[LmsEnrollments] ([Status], [LastActivityOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLessonFiles', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLessonFiles] (
		[LmsLessonFileId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsLessonId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[FileName] nvarchar(260) NULL,
		[ContentType] nvarchar(128) NULL,
		[SizeBytes] bigint NOT NULL CONSTRAINT [DF_LmsLessonFiles_SizeBytes] DEFAULT (0),
		[Data] varbinary(max) NULL,
		[UploadedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsLessonFiles] PRIMARY KEY CLUSTERED ([LmsLessonFileId] ASC),
		CONSTRAINT [FK_LmsLessonFiles_LmsLessonId] FOREIGN KEY ([LmsLessonId]) REFERENCES [dbo].[LmsLessons] ([LmsLessonId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonFiles_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsLessonFiles'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonFiles_DepartmentId] ON [dbo].[LmsLessonFiles] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonFiles_Lesson' AND object_id = OBJECT_ID('dbo.LmsLessonFiles'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonFiles_Lesson] ON [dbo].[LmsLessonFiles] ([LmsLessonId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCrewViewings', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCrewViewings] (
		[LmsCrewViewingId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[LmsLessonId] int NOT NULL,
		[StartedByUserId] nvarchar(128) NULL,
		[StartedOn] datetime2 NOT NULL,
		[ExpiresOn] datetime2 NOT NULL,
		[EndedOn] datetime2 NULL,
		[Secret] nvarchar(128) NOT NULL,
		CONSTRAINT [PK_LmsCrewViewings] PRIMARY KEY CLUSTERED ([LmsCrewViewingId] ASC),
		CONSTRAINT [FK_LmsCrewViewings_LmsCourseId] FOREIGN KEY ([LmsCourseId]) REFERENCES [dbo].[LmsCourses] ([LmsCourseId]),
		CONSTRAINT [FK_LmsCrewViewings_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId]),
		CONSTRAINT [FK_LmsCrewViewings_LmsLessonId] FOREIGN KEY ([LmsLessonId]) REFERENCES [dbo].[LmsLessons] ([LmsLessonId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCrewViewings_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCrewViewings'))
	CREATE NONCLUSTERED INDEX [IX_LmsCrewViewings_DepartmentId] ON [dbo].[LmsCrewViewings] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCrewViewings_Lesson' AND object_id = OBJECT_ID('dbo.LmsCrewViewings'))
	CREATE NONCLUSTERED INDEX [IX_LmsCrewViewings_Lesson] ON [dbo].[LmsCrewViewings] ([LmsLessonId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCrewViewings_Version' AND object_id = OBJECT_ID('dbo.LmsCrewViewings'))
	CREATE NONCLUSTERED INDEX [IX_LmsCrewViewings_Version] ON [dbo].[LmsCrewViewings] ([LmsCourseVersionId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCrewViewings_Course' AND object_id = OBJECT_ID('dbo.LmsCrewViewings'))
	CREATE NONCLUSTERED INDEX [IX_LmsCrewViewings_Course] ON [dbo].[LmsCrewViewings] ([LmsCourseId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLessonProgress', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLessonProgress] (
		[LmsLessonProgressId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsEnrollmentId] int NOT NULL,
		[LmsLessonId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsLessonProgress_Status] DEFAULT (0),
		[StartedOn] datetime2 NULL,
		[CompletedOn] datetime2 NULL,
		[SeatSeconds] int NOT NULL CONSTRAINT [DF_LmsLessonProgress_SeatSeconds] DEFAULT (0),
		[VideoPositionSeconds] int NOT NULL CONSTRAINT [DF_LmsLessonProgress_VideoPositionSeconds] DEFAULT (0),
		[WatchedPercent] int NOT NULL CONSTRAINT [DF_LmsLessonProgress_WatchedPercent] DEFAULT (0),
		[LastSeenOn] datetime2 NULL,
		[ResponseText] nvarchar(max) NULL,
		[LmsLessonFileId] int NULL,
		[LmsCrewViewingId] int NULL,
		CONSTRAINT [PK_LmsLessonProgress] PRIMARY KEY CLUSTERED ([LmsLessonProgressId] ASC),
		CONSTRAINT [FK_LmsLessonProgress_LmsEnrollmentId] FOREIGN KEY ([LmsEnrollmentId]) REFERENCES [dbo].[LmsEnrollments] ([LmsEnrollmentId]),
		CONSTRAINT [FK_LmsLessonProgress_LmsLessonId] FOREIGN KEY ([LmsLessonId]) REFERENCES [dbo].[LmsLessons] ([LmsLessonId]),
		CONSTRAINT [FK_LmsLessonProgress_LmsLessonFileId] FOREIGN KEY ([LmsLessonFileId]) REFERENCES [dbo].[LmsLessonFiles] ([LmsLessonFileId]),
		CONSTRAINT [FK_LmsLessonProgress_LmsCrewViewingId] FOREIGN KEY ([LmsCrewViewingId]) REFERENCES [dbo].[LmsCrewViewings] ([LmsCrewViewingId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLessonProgress_Enrollment_Lesson' AND object_id = OBJECT_ID('dbo.LmsLessonProgress'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsLessonProgress_Enrollment_Lesson] ON [dbo].[LmsLessonProgress] ([LmsEnrollmentId], [LmsLessonId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonProgress_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsLessonProgress'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonProgress_DepartmentId] ON [dbo].[LmsLessonProgress] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonProgress_Lesson' AND object_id = OBJECT_ID('dbo.LmsLessonProgress'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonProgress_Lesson] ON [dbo].[LmsLessonProgress] ([LmsLessonId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonProgress_File' AND object_id = OBJECT_ID('dbo.LmsLessonProgress'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonProgress_File] ON [dbo].[LmsLessonProgress] ([LmsLessonFileId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessonProgress_Viewing' AND object_id = OBJECT_ID('dbo.LmsLessonProgress'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessonProgress_Viewing] ON [dbo].[LmsLessonProgress] ([LmsCrewViewingId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsAssessmentAttempts', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsAssessmentAttempts] (
		[LmsAssessmentAttemptId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[LmsLessonId] int NOT NULL,
		[LmsEnrollmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[AttemptNumber] int NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_AttemptNumber] DEFAULT (1),
		[Status] int NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_Status] DEFAULT (0),
		[StartedOn] datetime2 NOT NULL,
		[DeadlineOn] datetime2 NULL,
		[SubmittedOn] datetime2 NULL,
		[Score] decimal(9,2) NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_Score] DEFAULT (0),
		[MaxScore] decimal(9,2) NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_MaxScore] DEFAULT (0),
		[Percent] decimal(9,2) NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_Percent] DEFAULT (0),
		[Passed] bit NULL,
		[IsPreTest] bit NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_IsPreTest] DEFAULT (0),
		[PassMark] int NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_PassMark] DEFAULT (0),
		[RevisionHash] nvarchar(64) NULL,
		[SnapshotJson] nvarchar(max) NULL,
		[AnswersJson] nvarchar(max) NULL,
		[ResultsJson] nvarchar(max) NULL,
		[GradedByUserId] nvarchar(128) NULL,
		[GradedOn] datetime2 NULL,
		[GradeNotes] nvarchar(2000) NULL,
		[Attested] bit NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_Attested] DEFAULT (0),
		[IpAddress] nvarchar(64) NULL,
		[UserAgent] nvarchar(512) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsAssessmentAttempts_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsAssessmentAttempts] PRIMARY KEY CLUSTERED ([LmsAssessmentAttemptId] ASC),
		CONSTRAINT [FK_LmsAssessmentAttempts_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId]),
		CONSTRAINT [FK_LmsAssessmentAttempts_LmsLessonId] FOREIGN KEY ([LmsLessonId]) REFERENCES [dbo].[LmsLessons] ([LmsLessonId]),
		CONSTRAINT [FK_LmsAssessmentAttempts_LmsEnrollmentId] FOREIGN KEY ([LmsEnrollmentId]) REFERENCES [dbo].[LmsEnrollments] ([LmsEnrollmentId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsAssessmentAttempts_Enrollment_Lesson' AND object_id = OBJECT_ID('dbo.LmsAssessmentAttempts'))
	CREATE NONCLUSTERED INDEX [IX_LmsAssessmentAttempts_Enrollment_Lesson] ON [dbo].[LmsAssessmentAttempts] ([LmsEnrollmentId], [LmsLessonId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsAssessmentAttempts_Lesson_Status' AND object_id = OBJECT_ID('dbo.LmsAssessmentAttempts'))
	CREATE NONCLUSTERED INDEX [IX_LmsAssessmentAttempts_Lesson_Status] ON [dbo].[LmsAssessmentAttempts] ([LmsLessonId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsAssessmentAttempts_Department_Status' AND object_id = OBJECT_ID('dbo.LmsAssessmentAttempts'))
	CREATE NONCLUSTERED INDEX [IX_LmsAssessmentAttempts_Department_Status] ON [dbo].[LmsAssessmentAttempts] ([DepartmentId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsAssessmentAttempts_Version' AND object_id = OBJECT_ID('dbo.LmsAssessmentAttempts'))
	CREATE NONCLUSTERED INDEX [IX_LmsAssessmentAttempts_Version] ON [dbo].[LmsAssessmentAttempts] ([LmsCourseVersionId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSurveyResponses', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSurveyResponses] (
		[LmsSurveyResponseId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[LmsLessonId] int NOT NULL,
		[AnswersJson] nvarchar(max) NULL,
		[SubmittedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsSurveyResponses] PRIMARY KEY CLUSTERED ([LmsSurveyResponseId] ASC),
		CONSTRAINT [FK_LmsSurveyResponses_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId]),
		CONSTRAINT [FK_LmsSurveyResponses_LmsLessonId] FOREIGN KEY ([LmsLessonId]) REFERENCES [dbo].[LmsLessons] ([LmsLessonId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSurveyResponses_Lesson' AND object_id = OBJECT_ID('dbo.LmsSurveyResponses'))
	CREATE NONCLUSTERED INDEX [IX_LmsSurveyResponses_Lesson] ON [dbo].[LmsSurveyResponses] ([LmsLessonId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSurveyResponses_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsSurveyResponses'))
	CREATE NONCLUSTERED INDEX [IX_LmsSurveyResponses_DepartmentId] ON [dbo].[LmsSurveyResponses] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSurveyResponses_Version' AND object_id = OBJECT_ID('dbo.LmsSurveyResponses'))
	CREATE NONCLUSTERED INDEX [IX_LmsSurveyResponses_Version] ON [dbo].[LmsSurveyResponses] ([LmsCourseVersionId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCrewViewingAttendees', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCrewViewingAttendees] (
		[LmsCrewViewingAttendeeId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCrewViewingId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[MarkedOn] datetime2 NOT NULL,
		[IpAddress] nvarchar(64) NULL,
		CONSTRAINT [PK_LmsCrewViewingAttendees] PRIMARY KEY CLUSTERED ([LmsCrewViewingAttendeeId] ASC),
		CONSTRAINT [FK_LmsCrewViewingAttendees_LmsCrewViewingId] FOREIGN KEY ([LmsCrewViewingId]) REFERENCES [dbo].[LmsCrewViewings] ([LmsCrewViewingId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCrewViewingAttendees_Viewing_User' AND object_id = OBJECT_ID('dbo.LmsCrewViewingAttendees'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCrewViewingAttendees_Viewing_User] ON [dbo].[LmsCrewViewingAttendees] ([LmsCrewViewingId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCrewViewingAttendees_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCrewViewingAttendees'))
	CREATE NONCLUSTERED INDEX [IX_LmsCrewViewingAttendees_DepartmentId] ON [dbo].[LmsCrewViewingAttendees] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsPathAssignments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsPathAssignments] (
		[LmsPathAssignmentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[PathKey] nvarchar(64) NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[AssignedByUserId] nvarchar(128) NULL,
		[AssignedOn] datetime2 NOT NULL,
		[DueOn] datetime2 NULL,
		[RemovedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsPathAssignments] PRIMARY KEY CLUSTERED ([LmsPathAssignmentId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathAssignments_Department_Path' AND object_id = OBJECT_ID('dbo.LmsPathAssignments'))
	CREATE NONCLUSTERED INDEX [IX_LmsPathAssignments_Department_Path] ON [dbo].[LmsPathAssignments] ([DepartmentId], [PathKey]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsPathAssignments_Department_User' AND object_id = OBJECT_ID('dbo.LmsPathAssignments'))
	CREATE NONCLUSTERED INDEX [IX_LmsPathAssignments_Department_User] ON [dbo].[LmsPathAssignments] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsDepartmentStates', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsDepartmentStates] (
		[LmsDepartmentStateId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LastEntitledOn] datetime2 NULL,
		[UpdatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsDepartmentStates] PRIMARY KEY CLUSTERED ([LmsDepartmentStateId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsDepartmentStates_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsDepartmentStates'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsDepartmentStates_DepartmentId] ON [dbo].[LmsDepartmentStates] ([DepartmentId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsDepartmentStates', 'U') IS NOT NULL DROP TABLE [dbo].[LmsDepartmentStates];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsPathAssignments', 'U') IS NOT NULL DROP TABLE [dbo].[LmsPathAssignments];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCrewViewingAttendees', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCrewViewingAttendees];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsSurveyResponses', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSurveyResponses];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsAssessmentAttempts', 'U') IS NOT NULL DROP TABLE [dbo].[LmsAssessmentAttempts];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessonProgress', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLessonProgress];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCrewViewings', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCrewViewings];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessonFiles', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLessonFiles];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsEnrollments', 'U') IS NOT NULL DROP TABLE [dbo].[LmsEnrollments];");
		}
	}
}
