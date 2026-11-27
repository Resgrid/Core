using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 class sessions (protocols-training-knowledge-plan §13 T4 items 1 and 4, §13a T4 Events; LMS add-on): LmsClassSessions (a
	/// calendar item, course link, enrolment rules, capacity and waitlist, rotating QR and GPS sign-in, the OSHA roster's content summary
	/// and retention hold, sign-off), LmsClassEnrollments (one row per member and session: enrolment, waitlist, attendance, sign-in, hours
	/// and the sign-off snapshot of name and job title) and LmsClassInstructors (members or outside instructors with qualifications and
	/// hours taught). No foreign keys (an LMS table references baseline ids only, §17.3); LmsClassesDepartmentCleanup removes the rows.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(378)]
	public class M0378_AddLmsClassSessions : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassSessions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassSessions] (
		[LmsClassSessionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[CalendarItemId] int NULL,
		[SeriesKey] nvarchar(64) NULL,
		[SeriesIndex] int NOT NULL CONSTRAINT [DF_LmsClassSessions_SeriesIndex] DEFAULT (0),
		[Title] nvarchar(250) NOT NULL,
		[Description] nvarchar(4000) NULL,
		[ContentSummary] nvarchar(4000) NULL,
		[CourseKind] int NOT NULL CONSTRAINT [DF_LmsClassSessions_CourseKind] DEFAULT (0),
		[LmsCourseId] int NULL,
		[TrainingId] int NULL,
		[LmsLessonPlanId] int NULL,
		[LmsLessonPlanVersionId] int NULL,
		[CategoryId] int NULL,
		[CourseCode] nvarchar(100) NULL,
		[StartsOn] datetime2 NOT NULL,
		[EndsOn] datetime2 NOT NULL,
		[Location] nvarchar(500) NULL,
		[MeetingUrl] nvarchar(1000) NULL,
		[Capacity] int NOT NULL CONSTRAINT [DF_LmsClassSessions_Capacity] DEFAULT (0),
		[WaitlistEnabled] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_WaitlistEnabled] DEFAULT (0),
		[EnrollmentOpen] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_EnrollmentOpen] DEFAULT (0),
		[RequiresApproval] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_RequiresApproval] DEFAULT (0),
		[CutoffDays] int NOT NULL CONSTRAINT [DF_LmsClassSessions_CutoffDays] DEFAULT (0),
		[AllowSelfUnenroll] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_AllowSelfUnenroll] DEFAULT (0),
		[RestrictGroupIds] nvarchar(1000) NULL,
		[VisibleGroupIds] nvarchar(1000) NULL,
		[Tags] nvarchar(500) NULL,
		[ReminderHours] int NOT NULL CONSTRAINT [DF_LmsClassSessions_ReminderHours] DEFAULT (0),
		[QrEnabled] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_QrEnabled] DEFAULT (0),
		[QrSecret] nvarchar(128) NULL,
		[GpsRequired] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_GpsRequired] DEFAULT (0),
		[GpsLatitude] decimal(9,6) NULL,
		[GpsLongitude] decimal(9,6) NULL,
		[GpsRadiusMeters] int NOT NULL CONSTRAINT [DF_LmsClassSessions_GpsRadiusMeters] DEFAULT (0),
		[PostClassEvaluationKind] int NOT NULL CONSTRAINT [DF_LmsClassSessions_PostClassEvaluationKind] DEFAULT (0),
		[PostClassEvaluationRef] nvarchar(128) NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsClassSessions_Status] DEFAULT (0),
		[CancelledOn] datetime2 NULL,
		[CancelReason] nvarchar(500) NULL,
		[SignedOffOn] datetime2 NULL,
		[SignedOffByUserId] nvarchar(128) NULL,
		[RetentionYears] int NULL,
		[RetainUntil] datetime2 NULL,
		[Currency] nvarchar(3) NULL,
		[LmsTrainingPlanItemId] int NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsClassSessions_IsDeleted] DEFAULT (0),
		[DeletedOn] datetime2 NULL,
		[DeletedByUserId] nvarchar(128) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsClassSessions] PRIMARY KEY CLUSTERED ([LmsClassSessionId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassSessions_Department_Starts' AND object_id = OBJECT_ID('dbo.LmsClassSessions'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassSessions_Department_Starts] ON [dbo].[LmsClassSessions] ([DepartmentId], [StartsOn]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassSessions_Status_Starts' AND object_id = OBJECT_ID('dbo.LmsClassSessions'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassSessions_Status_Starts] ON [dbo].[LmsClassSessions] ([Status], [IsDeleted], [StartsOn]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassSessions_CalendarItem' AND object_id = OBJECT_ID('dbo.LmsClassSessions'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassSessions_CalendarItem] ON [dbo].[LmsClassSessions] ([DepartmentId], [CalendarItemId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassSessions_Series' AND object_id = OBJECT_ID('dbo.LmsClassSessions'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassSessions_Series] ON [dbo].[LmsClassSessions] ([DepartmentId], [SeriesKey]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassEnrollments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassEnrollments] (
		[LmsClassEnrollmentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsClassEnrollments_Status] DEFAULT (0),
		[WaitlistPosition] int NULL,
		[Attendance] int NOT NULL CONSTRAINT [DF_LmsClassEnrollments_Attendance] DEFAULT (0),
		[IsWalkIn] bit NOT NULL CONSTRAINT [DF_LmsClassEnrollments_IsWalkIn] DEFAULT (0),
		[CheckInMethod] int NOT NULL CONSTRAINT [DF_LmsClassEnrollments_CheckInMethod] DEFAULT (0),
		[CheckInOn] datetime2 NULL,
		[CheckOutOn] datetime2 NULL,
		[CheckInLatitude] decimal(9,6) NULL,
		[CheckInLongitude] decimal(9,6) NULL,
		[ImportedMinutes] int NULL,
		[Hours] decimal(9,2) NULL,
		[HoursOverride] decimal(9,2) NULL,
		[MemberName] nvarchar(200) NULL,
		[JobTitle] nvarchar(200) NULL,
		[LearningRecordId] int NULL,
		[TimecardOfferedOn] datetime2 NULL,
		[ReminderSentOn] datetime2 NULL,
		[EnrolledOn] datetime2 NOT NULL,
		[EnrolledByUserId] nvarchar(128) NULL,
		[DecidedOn] datetime2 NULL,
		[DecidedByUserId] nvarchar(128) NULL,
		[PromotedOn] datetime2 NULL,
		[WithdrawnOn] datetime2 NULL,
		[Note] nvarchar(500) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsClassEnrollments] PRIMARY KEY CLUSTERED ([LmsClassEnrollmentId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsClassEnrollments_Session_User' AND object_id = OBJECT_ID('dbo.LmsClassEnrollments'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsClassEnrollments_Session_User] ON [dbo].[LmsClassEnrollments] ([LmsClassSessionId], [UserId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassEnrollments_Department_User' AND object_id = OBJECT_ID('dbo.LmsClassEnrollments'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassEnrollments_Department_User] ON [dbo].[LmsClassEnrollments] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassInstructors', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassInstructors] (
		[LmsClassInstructorId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[UserId] nvarchar(128) NULL,
		[Name] nvarchar(200) NULL,
		[Qualifications] nvarchar(500) NULL,
		[Role] int NOT NULL CONSTRAINT [DF_LmsClassInstructors_Role] DEFAULT (0),
		[HoursTaught] decimal(9,2) NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsClassInstructors_SortOrder] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsClassInstructors] PRIMARY KEY CLUSTERED ([LmsClassInstructorId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassInstructors_Session' AND object_id = OBJECT_ID('dbo.LmsClassInstructors'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassInstructors_Session] ON [dbo].[LmsClassInstructors] ([DepartmentId], [LmsClassSessionId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassInstructors_User' AND object_id = OBJECT_ID('dbo.LmsClassInstructors'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassInstructors_User] ON [dbo].[LmsClassInstructors] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassInstructors', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassInstructors];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassEnrollments', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassEnrollments];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassSessions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassSessions];");
		}
	}
}
