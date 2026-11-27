using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T5 item 2 external learners (protocols-training-knowledge-plan §13 T5 item 2, §17.4 decision 10, §13a branded learner portal;
	/// LMS add-on): LmsLearnerPortals (the department's portal key, switches and accent), LmsExternalLearners (a learner-only identity per
	/// department: never an AspNetUsers row, never a member) and LmsExternalLearnerTokens (hashed, single-use e-mailed tokens). No foreign
	/// keys (§17.3). Existence-guarded for safe retry.
	/// </summary>
	[Migration(396)]
	public class M0396_AddLmsExternalLearners : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLearnerPortals', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLearnerPortals] (
		[LmsLearnerPortalId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[PortalKey] nvarchar(32) NOT NULL,
		[IsEnabled] bit NOT NULL CONSTRAINT [DF_LmsLearnerPortals_IsEnabled] DEFAULT (0),
		[SelfRegistration] bit NOT NULL CONSTRAINT [DF_LmsLearnerPortals_SelfRegistration] DEFAULT (0),
		[PublicListing] bit NOT NULL CONSTRAINT [DF_LmsLearnerPortals_PublicListing] DEFAULT (0),
		[AccentColor] nvarchar(7) NULL,
		[WelcomeText] nvarchar(1000) NULL,
		[HideLogo] bit NOT NULL CONSTRAINT [DF_LmsLearnerPortals_HideLogo] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsLearnerPortals] PRIMARY KEY CLUSTERED ([LmsLearnerPortalId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLearnerPortals_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsLearnerPortals'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsLearnerPortals_DepartmentId] ON [dbo].[LmsLearnerPortals] ([DepartmentId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLearnerPortals_PortalKey' AND object_id = OBJECT_ID('dbo.LmsLearnerPortals'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsLearnerPortals_PortalKey] ON [dbo].[LmsLearnerPortals] ([PortalKey]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsExternalLearners] (
		[LmsExternalLearnerId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LearnerUserId] nvarchar(64) NOT NULL,
		[Email] nvarchar(256) NULL,
		[FirstName] nvarchar(100) NULL,
		[LastName] nvarchar(100) NULL,
		[Organization] nvarchar(200) NULL,
		[SponsorContactId] nvarchar(128) NULL,
		[BillingContactId] nvarchar(128) NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsExternalLearners_Status] DEFAULT (0),
		[Source] int NOT NULL CONSTRAINT [DF_LmsExternalLearners_Source] DEFAULT (0),
		[PasswordHash] nvarchar(500) NULL,
		[SecurityStamp] nvarchar(64) NOT NULL,
		[EmailConfirmedOn] datetime2 NULL,
		[AccessFailedCount] int NOT NULL CONSTRAINT [DF_LmsExternalLearners_AccessFailedCount] DEFAULT (0),
		[LockoutEndsOn] datetime2 NULL,
		[LastSignInOn] datetime2 NULL,
		[InvitedByUserId] nvarchar(128) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[UpdatedOn] datetime2 NULL,
		[ErasedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsExternalLearners] PRIMARY KEY CLUSTERED ([LmsExternalLearnerId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsExternalLearners_LearnerUserId' AND object_id = OBJECT_ID('dbo.LmsExternalLearners'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsExternalLearners_LearnerUserId] ON [dbo].[LmsExternalLearners] ([LearnerUserId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsExternalLearners_Email' AND object_id = OBJECT_ID('dbo.LmsExternalLearners'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsExternalLearners_Email] ON [dbo].[LmsExternalLearners] ([DepartmentId], [Email]) WHERE [Email] IS NOT NULL;");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsExternalLearners_Department_Status' AND object_id = OBJECT_ID('dbo.LmsExternalLearners'))
	CREATE NONCLUSTERED INDEX [IX_LmsExternalLearners_Department_Status] ON [dbo].[LmsExternalLearners] ([DepartmentId], [Status]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsExternalLearnerTokens', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsExternalLearnerTokens] (
		[LmsExternalLearnerTokenId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsExternalLearnerId] int NOT NULL,
		[Purpose] int NOT NULL CONSTRAINT [DF_LmsExternalLearnerTokens_Purpose] DEFAULT (0),
		[TokenHash] nvarchar(64) NOT NULL,
		[LmsClassSessionId] int NULL,
		[ExpiresOn] datetime2 NOT NULL,
		[UsedOn] datetime2 NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsExternalLearnerTokens] PRIMARY KEY CLUSTERED ([LmsExternalLearnerTokenId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsExternalLearnerTokens_TokenHash' AND object_id = OBJECT_ID('dbo.LmsExternalLearnerTokens'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsExternalLearnerTokens_TokenHash] ON [dbo].[LmsExternalLearnerTokens] ([TokenHash]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsExternalLearnerTokens_Learner' AND object_id = OBJECT_ID('dbo.LmsExternalLearnerTokens'))
	CREATE NONCLUSTERED INDEX [IX_LmsExternalLearnerTokens_Learner] ON [dbo].[LmsExternalLearnerTokens] ([DepartmentId], [LmsExternalLearnerId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalLearnerTokens', 'U') IS NOT NULL DROP TABLE [dbo].[LmsExternalLearnerTokens];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NOT NULL DROP TABLE [dbo].[LmsExternalLearners];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLearnerPortals', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLearnerPortals];");
		}
	}
}
