using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 7B (product-owner decision 2026-11-25, permission 135 RecordTrainingCompletions): completions an instructor records for members.
	/// TrainingCompletionRecordings (one row per member and recording: target, source, note, attestation version, the records produced, the
	/// void) and the completion metadata on the ledger (LearningRecords.CompletionMethod, RecordedByUserId, RecordedOn, RecordedCompletionId)
	/// and on the baseline assignment (TrainingUsers.CompletionMethod, RecordedCompletionId). Baseline (free); no foreign keys (the ledger
	/// references its sources by id). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(400)]
	public class M0400_AddTrainingCompletionRecordings : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.TrainingCompletionRecordings', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[TrainingCompletionRecordings] (
		[TrainingCompletionRecordingId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[TargetKind] int NOT NULL CONSTRAINT [DF_TrainingCompletionRecordings_TargetKind] DEFAULT (0),
		[TargetId] int NOT NULL,
		[TargetVersionId] int NULL,
		[TargetName] nvarchar(250) NULL,
		[SourceKind] int NOT NULL CONSTRAINT [DF_TrainingCompletionRecordings_SourceKind] DEFAULT (0),
		[SourceRefId] nvarchar(64) NULL,
		[BatchKey] nvarchar(64) NULL,
		[Note] nvarchar(1000) NULL,
		[AttestationVersion] nvarchar(32) NOT NULL,
		[QuizWaived] bit NOT NULL CONSTRAINT [DF_TrainingCompletionRecordings_QuizWaived] DEFAULT (0),
		[CompletedOn] datetime2 NOT NULL,
		[TrainingUserId] int NULL,
		[Cycle] int NULL,
		[EnrollmentId] int NULL,
		[LearningRecordId] int NULL,
		[PersonnelCertificationId] int NULL,
		[RecordedByUserId] nvarchar(128) NOT NULL,
		[RecordedOn] datetime2 NOT NULL,
		[VoidedOn] datetime2 NULL,
		[VoidedByUserId] nvarchar(128) NULL,
		[VoidReason] nvarchar(500) NULL,
		CONSTRAINT [PK_TrainingCompletionRecordings] PRIMARY KEY CLUSTERED ([TrainingCompletionRecordingId] ASC)
	);
END");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrainingCompletionRecordings_User' AND object_id = OBJECT_ID('dbo.TrainingCompletionRecordings'))
	CREATE NONCLUSTERED INDEX [IX_TrainingCompletionRecordings_User] ON [dbo].[TrainingCompletionRecordings] ([DepartmentId], [UserId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrainingCompletionRecordings_Target' AND object_id = OBJECT_ID('dbo.TrainingCompletionRecordings'))
	CREATE NONCLUSTERED INDEX [IX_TrainingCompletionRecordings_Target] ON [dbo].[TrainingCompletionRecordings] ([DepartmentId], [TargetKind], [TargetId]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrainingCompletionRecordings_Source' AND object_id = OBJECT_ID('dbo.TrainingCompletionRecordings'))
	CREATE NONCLUSTERED INDEX [IX_TrainingCompletionRecordings_Source] ON [dbo].[TrainingCompletionRecordings] ([DepartmentId], [SourceKind], [SourceRefId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'CompletionMethod') IS NULL
	ALTER TABLE [dbo].[LearningRecords] ADD [CompletionMethod] int NOT NULL CONSTRAINT [DF_LearningRecords_CompletionMethod] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'RecordedByUserId') IS NULL ALTER TABLE [dbo].[LearningRecords] ADD [RecordedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'RecordedOn') IS NULL ALTER TABLE [dbo].[LearningRecords] ADD [RecordedOn] datetime2 NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'RecordedCompletionId') IS NULL ALTER TABLE [dbo].[LearningRecords] ADD [RecordedCompletionId] int NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.TrainingUsers', 'U') IS NOT NULL AND COL_LENGTH('dbo.TrainingUsers', 'CompletionMethod') IS NULL
	ALTER TABLE [dbo].[TrainingUsers] ADD [CompletionMethod] int NOT NULL CONSTRAINT [DF_TrainingUsers_CompletionMethod] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.TrainingUsers', 'U') IS NOT NULL AND COL_LENGTH('dbo.TrainingUsers', 'RecordedCompletionId') IS NULL ALTER TABLE [dbo].[TrainingUsers] ADD [RecordedCompletionId] int NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('dbo.TrainingUsers', 'RecordedCompletionId') IS NOT NULL ALTER TABLE [dbo].[TrainingUsers] DROP COLUMN [RecordedCompletionId];");
			Execute.Sql(@"
IF OBJECT_ID('DF_TrainingUsers_CompletionMethod', 'D') IS NOT NULL ALTER TABLE [dbo].[TrainingUsers] DROP CONSTRAINT [DF_TrainingUsers_CompletionMethod];
IF COL_LENGTH('dbo.TrainingUsers', 'CompletionMethod') IS NOT NULL ALTER TABLE [dbo].[TrainingUsers] DROP COLUMN [CompletionMethod];");

			foreach (var column in new[] { "RecordedCompletionId", "RecordedOn", "RecordedByUserId" })
				Execute.Sql($"IF COL_LENGTH('dbo.LearningRecords', '{column}') IS NOT NULL ALTER TABLE [dbo].[LearningRecords] DROP COLUMN [{column}];");
			Execute.Sql(@"
IF OBJECT_ID('DF_LearningRecords_CompletionMethod', 'D') IS NOT NULL ALTER TABLE [dbo].[LearningRecords] DROP CONSTRAINT [DF_LearningRecords_CompletionMethod];
IF COL_LENGTH('dbo.LearningRecords', 'CompletionMethod') IS NOT NULL ALTER TABLE [dbo].[LearningRecords] DROP COLUMN [CompletionMethod];");

			Execute.Sql("IF OBJECT_ID('dbo.TrainingCompletionRecordings', 'U') IS NOT NULL DROP TABLE [dbo].[TrainingCompletionRecordings];");
		}
	}
}
