using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9B: LearningRecordReviewEvents, the review history of a member's training hours submission (submitted, self-reported, approved,
	/// denied, more information asked for, resubmitted) with the member's or reviewer's note. Baseline (free); no foreign keys (the ledger's
	/// rule); purged with the department. Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(409)]
	public class M0409_AddLearningRecordReviewEvents : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecordReviewEvents', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LearningRecordReviewEvents] (
		[LearningRecordReviewEventId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LearningRecordId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LearningRecordReviewEvents_Kind] DEFAULT (0),
		[ActorUserId] nvarchar(128) NULL,
		[Note] nvarchar(1000) NULL,
		[Hours] decimal(9, 2) NULL,
		[FileCount] int NOT NULL CONSTRAINT [DF_LearningRecordReviewEvents_FileCount] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LearningRecordReviewEvents] PRIMARY KEY CLUSTERED ([LearningRecordReviewEventId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecordReviewEvents_Record' AND object_id = OBJECT_ID('dbo.LearningRecordReviewEvents'))
	CREATE NONCLUSTERED INDEX [IX_LearningRecordReviewEvents_Record] ON [dbo].[LearningRecordReviewEvents] ([DepartmentId], [LearningRecordId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecordReviewEvents_User' AND object_id = OBJECT_ID('dbo.LearningRecordReviewEvents'))
	CREATE NONCLUSTERED INDEX [IX_LearningRecordReviewEvents_User] ON [dbo].[LearningRecordReviewEvents] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecordReviewEvents', 'U') IS NOT NULL DROP TABLE [dbo].[LearningRecordReviewEvents];");
		}
	}
}
