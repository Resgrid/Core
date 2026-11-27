using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 7B (LMS add-on): e-mails to external learners about their public class places. LmsLearnerNotices (the once-only claim of each
	/// mail: learner, session, kind and key, unique) and, on LmsExternalLearners, the learner's Language, RemindersOptOut and the random
	/// UnsubscribeKey of the reminder unsubscribe link. No foreign keys (§17.3). Existence-guarded for safe retry; Down removes exactly what
	/// Up added.
	/// </summary>
	[Migration(402)]
	public class M0402_AddLmsLearnerNotices : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLearnerNotices', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLearnerNotices] (
		[LmsLearnerNoticeId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsExternalLearnerId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[Kind] int NOT NULL,
		[NoticeKey] nvarchar(100) NOT NULL,
		[ClaimedOn] datetime2 NOT NULL,
		[SentOn] datetime2 NULL,
		[Skipped] nvarchar(100) NULL,
		CONSTRAINT [PK_LmsLearnerNotices] PRIMARY KEY CLUSTERED ([LmsLearnerNoticeId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsLearnerNotices_Key' AND object_id = OBJECT_ID('dbo.LmsLearnerNotices'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsLearnerNotices_Key] ON [dbo].[LmsLearnerNotices] ([DepartmentId], [LmsExternalLearnerId], [LmsClassSessionId], [Kind], [NoticeKey]);");

			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsExternalLearners', 'Language') IS NULL ALTER TABLE [dbo].[LmsExternalLearners] ADD [Language] nvarchar(16) NULL;");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsExternalLearners', 'RemindersOptOut') IS NULL
	ALTER TABLE [dbo].[LmsExternalLearners] ADD [RemindersOptOut] bit NOT NULL CONSTRAINT [DF_LmsExternalLearners_RemindersOptOut] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.LmsExternalLearners', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsExternalLearners', 'UnsubscribeKey') IS NULL ALTER TABLE [dbo].[LmsExternalLearners] ADD [UnsubscribeKey] nvarchar(128) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.LmsExternalLearners', 'UnsubscribeKey') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP COLUMN [UnsubscribeKey];");
			Execute.Sql(@"
IF OBJECT_ID('DF_LmsExternalLearners_RemindersOptOut', 'D') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP CONSTRAINT [DF_LmsExternalLearners_RemindersOptOut];
IF COL_LENGTH('dbo.LmsExternalLearners', 'RemindersOptOut') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP COLUMN [RemindersOptOut];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsExternalLearners', 'Language') IS NOT NULL ALTER TABLE [dbo].[LmsExternalLearners] DROP COLUMN [Language];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLearnerNotices', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLearnerNotices];");
		}
	}
}
