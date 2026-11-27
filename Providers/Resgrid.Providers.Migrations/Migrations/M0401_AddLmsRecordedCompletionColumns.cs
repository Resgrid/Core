using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 7B (permission 135, LMS add-on): an LMS course completion recorded by an instructor. LmsEnrollments.CompletionMethod and
	/// RecordedCompletionId (the enrolment the recording completed) and LmsLessonProgress.RecordedCompletionId (each lesson the recording marked
	/// complete, so a void undoes exactly those). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(401)]
	public class M0401_AddLmsRecordedCompletionColumns : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsEnrollments', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsEnrollments', 'CompletionMethod') IS NULL
	ALTER TABLE [dbo].[LmsEnrollments] ADD [CompletionMethod] int NOT NULL CONSTRAINT [DF_LmsEnrollments_CompletionMethod] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.LmsEnrollments', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsEnrollments', 'RecordedCompletionId') IS NULL ALTER TABLE [dbo].[LmsEnrollments] ADD [RecordedCompletionId] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessonProgress', 'U') IS NOT NULL AND COL_LENGTH('dbo.LmsLessonProgress', 'RecordedCompletionId') IS NULL ALTER TABLE [dbo].[LmsLessonProgress] ADD [RecordedCompletionId] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.LmsLessonProgress', 'RecordedCompletionId') IS NOT NULL ALTER TABLE [dbo].[LmsLessonProgress] DROP COLUMN [RecordedCompletionId];");
			Execute.Sql("IF COL_LENGTH('dbo.LmsEnrollments', 'RecordedCompletionId') IS NOT NULL ALTER TABLE [dbo].[LmsEnrollments] DROP COLUMN [RecordedCompletionId];");
			Execute.Sql(@"
IF OBJECT_ID('DF_LmsEnrollments_CompletionMethod', 'D') IS NOT NULL ALTER TABLE [dbo].[LmsEnrollments] DROP CONSTRAINT [DF_LmsEnrollments_CompletionMethod];
IF COL_LENGTH('dbo.LmsEnrollments', 'CompletionMethod') IS NOT NULL ALTER TABLE [dbo].[LmsEnrollments] DROP COLUMN [CompletionMethod];");
		}
	}
}
