using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 skill notification digest (§13a "notification digests"; LMS add-on): LmsSkillNotifications, the queue worker 96 drains when
	/// due (immediately, or at the department's daily or weekly digest time); the due index covers unsent rows only.
	/// No foreign keys (§17.3: LMS tables reference baseline ids only); LmsSkillsDepartmentCleanup removes the rows. Existence-guarded for safe
	/// retry.
	/// </summary>
	[Migration(410)]
	public class M0410_AddLmsSkillNotifications : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsSkillNotifications', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsSkillNotifications] (
		[LmsSkillNotificationId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsSkillNotifications_Kind] DEFAULT (0),
		[Text] nvarchar(1000) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[DueOn] datetime2 NOT NULL,
		[ClaimedOn] datetime2 NULL,
		[SentOn] datetime2 NULL,
		[Skipped] bit NOT NULL CONSTRAINT [DF_LmsSkillNotifications_Skipped] DEFAULT (0),
		CONSTRAINT [PK_LmsSkillNotifications] PRIMARY KEY CLUSTERED ([LmsSkillNotificationId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillNotifications_Due' AND object_id = OBJECT_ID('dbo.LmsSkillNotifications'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillNotifications_Due] ON [dbo].[LmsSkillNotifications] ([DueOn]) WHERE [SentOn] IS NULL;");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsSkillNotifications_Department_User' AND object_id = OBJECT_ID('dbo.LmsSkillNotifications'))
	CREATE NONCLUSTERED INDEX [IX_LmsSkillNotifications_Department_User] ON [dbo].[LmsSkillNotifications] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsSkillNotifications', 'U') IS NOT NULL DROP TABLE [dbo].[LmsSkillNotifications];");
		}
	}
}
