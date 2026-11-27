using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 item 2 paid public classes (R-C13, §25.4a item 12; LMS add-on): LmsPublicClasses (a class session listed on the learner
	/// portal with an optional fee per external learner and the Business Operations rate item its invoice lines carry), LmsClassFees (one
	/// external learner's fee on a roster: waiver, sponsoring organization, the invoice raised; its own table so a roster save never
	/// overwrites an invoice link) and LmsExternalCourseAccess (which department courses external learners may take). The class session,
	/// roster and course rows are not altered. No foreign keys (§17.3). Existence-guarded for safe retry.
	/// </summary>
	[Migration(397)]
	public class M0397_AddLmsPublicClasses : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsPublicClasses', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsPublicClasses] (
		[LmsPublicClassId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[IsPublic] bit NOT NULL CONSTRAINT [DF_LmsPublicClasses_IsPublic] DEFAULT (0),
		[FeeAmount] decimal(14,2) NULL,
		[Currency] nvarchar(3) NULL,
		[RateCardItemId] nvarchar(128) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsPublicClasses] PRIMARY KEY CLUSTERED ([LmsPublicClassId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsPublicClasses_Session' AND object_id = OBJECT_ID('dbo.LmsPublicClasses'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsPublicClasses_Session] ON [dbo].[LmsPublicClasses] ([DepartmentId], [LmsClassSessionId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassFees', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassFees] (
		[LmsClassFeeId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[LmsClassEnrollmentId] int NOT NULL,
		[LearnerUserId] nvarchar(64) NOT NULL,
		[Amount] decimal(14,2) NOT NULL CONSTRAINT [DF_LmsClassFees_Amount] DEFAULT (0),
		[Currency] nvarchar(3) NULL,
		[Waived] bit NOT NULL CONSTRAINT [DF_LmsClassFees_Waived] DEFAULT (0),
		[SponsorContactId] nvarchar(128) NULL,
		[InvoiceId] nvarchar(128) NULL,
		[InvoicedOn] datetime2 NULL,
		[CreatedOn] datetime2 NOT NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsClassFees] PRIMARY KEY CLUSTERED ([LmsClassFeeId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsClassFees_Enrollment' AND object_id = OBJECT_ID('dbo.LmsClassFees'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsClassFees_Enrollment] ON [dbo].[LmsClassFees] ([DepartmentId], [LmsClassEnrollmentId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassFees_Session' AND object_id = OBJECT_ID('dbo.LmsClassFees'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassFees_Session] ON [dbo].[LmsClassFees] ([DepartmentId], [LmsClassSessionId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassFees_Learner' AND object_id = OBJECT_ID('dbo.LmsClassFees'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassFees_Learner] ON [dbo].[LmsClassFees] ([DepartmentId], [LearnerUserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsExternalCourseAccess', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsExternalCourseAccess] (
		[LmsExternalCourseAccessId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[Access] int NOT NULL CONSTRAINT [DF_LmsExternalCourseAccess_Access] DEFAULT (0),
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsExternalCourseAccess] PRIMARY KEY CLUSTERED ([LmsExternalCourseAccessId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsExternalCourseAccess_Course' AND object_id = OBJECT_ID('dbo.LmsExternalCourseAccess'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsExternalCourseAccess_Course] ON [dbo].[LmsExternalCourseAccess] ([DepartmentId], [LmsCourseId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalCourseAccess', 'U') IS NOT NULL DROP TABLE [dbo].[LmsExternalCourseAccess];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassFees', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassFees];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsPublicClasses', 'U') IS NOT NULL DROP TABLE [dbo].[LmsPublicClasses];");
		}
	}
}
