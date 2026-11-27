using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9B: TrainingReviewDigests, the daily reviewer digest claims of training hours submissions (worker 85): one row per department and
	/// department-local day, inserted before the notices go out (unique), with the reviewer and submission counts. Baseline (free); no foreign
	/// keys; purged with the department. Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(410)]
	public class M0410_AddTrainingReviewDigests : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.TrainingReviewDigests', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[TrainingReviewDigests] (
		[TrainingReviewDigestId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DigestDate] datetime2 NOT NULL,
		[Reviewers] int NOT NULL CONSTRAINT [DF_TrainingReviewDigests_Reviewers] DEFAULT (0),
		[Submissions] int NOT NULL CONSTRAINT [DF_TrainingReviewDigests_Submissions] DEFAULT (0),
		[SentOn] datetime2 NOT NULL,
		CONSTRAINT [PK_TrainingReviewDigests] PRIMARY KEY CLUSTERED ([TrainingReviewDigestId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrainingReviewDigests_Day' AND object_id = OBJECT_ID('dbo.TrainingReviewDigests'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_TrainingReviewDigests_Day] ON [dbo].[TrainingReviewDigests] ([DepartmentId], [DigestDate]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.TrainingReviewDigests', 'U') IS NOT NULL DROP TABLE [dbo].[TrainingReviewDigests];");
		}
	}
}
