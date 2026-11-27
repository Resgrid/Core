using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Round 9B (training hours submissions with evidence and optional review): the review state of a member submission on the ledger
	/// (LearningRecords.ReviewState, SubmittedOn) and several evidence files per submission (LearningRecordFiles.LearningRecordId, SizeBytes,
	/// Sha256, EvidenceKind, ScanState). T1 submissions get their state from their status (Pending waits for review, Verified was approved,
	/// Rejected was denied) and their single file is linked by EvidenceFileId; files stored before scanning existed read as Skipped (3).
	/// Baseline (free); no foreign keys (the ledger's rule). Existence-guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(408)]
	public class M0408_AddTrainingSubmissionReview : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'ReviewState') IS NULL ALTER TABLE [dbo].[LearningRecords] ADD [ReviewState] int NULL;");
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecords', 'SubmittedOn') IS NULL ALTER TABLE [dbo].[LearningRecords] ADD [SubmittedOn] datetime2 NULL;");

			Execute.Sql("IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecordFiles', 'LearningRecordId') IS NULL ALTER TABLE [dbo].[LearningRecordFiles] ADD [LearningRecordId] int NULL;");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecordFiles', 'SizeBytes') IS NULL
	ALTER TABLE [dbo].[LearningRecordFiles] ADD [SizeBytes] bigint NOT NULL CONSTRAINT [DF_LearningRecordFiles_SizeBytes] DEFAULT (0);");
			Execute.Sql("IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecordFiles', 'Sha256') IS NULL ALTER TABLE [dbo].[LearningRecordFiles] ADD [Sha256] nvarchar(64) NULL;");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecordFiles', 'EvidenceKind') IS NULL
	ALTER TABLE [dbo].[LearningRecordFiles] ADD [EvidenceKind] int NOT NULL CONSTRAINT [DF_LearningRecordFiles_EvidenceKind] DEFAULT (0);");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND COL_LENGTH('dbo.LearningRecordFiles', 'ScanState') IS NULL
	ALTER TABLE [dbo].[LearningRecordFiles] ADD [ScanState] int NOT NULL CONSTRAINT [DF_LearningRecordFiles_ScanState] DEFAULT (3);");

			// T1's single evidence file is linked to its record, and its size read once (lists never read the bytes).
			Execute.Sql(@"
IF COL_LENGTH('dbo.LearningRecordFiles', 'LearningRecordId') IS NOT NULL
	UPDATE f SET f.[LearningRecordId] = r.[LearningRecordId]
	FROM [dbo].[LearningRecordFiles] f
	INNER JOIN [dbo].[LearningRecords] r ON r.[DepartmentId] = f.[DepartmentId] AND r.[EvidenceFileId] = f.[LearningRecordFileId]
	WHERE f.[LearningRecordId] IS NULL;");
			Execute.Sql(@"
IF COL_LENGTH('dbo.LearningRecordFiles', 'SizeBytes') IS NOT NULL
	UPDATE [dbo].[LearningRecordFiles] SET [SizeBytes] = DATALENGTH([Data]) WHERE [SizeBytes] = 0 AND [Data] IS NOT NULL;");

			// T1 submissions (SourceKind 3): Pending waits for review (0), Verified was approved (2), or accepted unreviewed (4) when nobody
			// verified it, Rejected was denied (3). Voided ones keep no state.
			Execute.Sql(@"
IF COL_LENGTH('dbo.LearningRecords', 'ReviewState') IS NOT NULL
	UPDATE [dbo].[LearningRecords]
	SET [ReviewState] = CASE [Status] WHEN 0 THEN 0 WHEN 2 THEN 3 ELSE CASE WHEN [VerifiedByUserId] IS NULL THEN 4 ELSE 2 END END,
		[SubmittedOn] = COALESCE([SubmittedOn], [CreatedOn])
	WHERE [SourceKind] = 3 AND [ReviewState] IS NULL AND [Status] IN (0, 1, 2);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecords', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecords_Review' AND object_id = OBJECT_ID('dbo.LearningRecords'))
	CREATE NONCLUSTERED INDEX [IX_LearningRecords_Review] ON [dbo].[LearningRecords] ([DepartmentId], [SourceKind], [Status], [ReviewState]);");
			Execute.Sql(@"
IF OBJECT_ID('dbo.LearningRecordFiles', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecordFiles_Record' AND object_id = OBJECT_ID('dbo.LearningRecordFiles'))
	CREATE NONCLUSTERED INDEX [IX_LearningRecordFiles_Record] ON [dbo].[LearningRecordFiles] ([DepartmentId], [LearningRecordId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecordFiles_Record' AND object_id = OBJECT_ID('dbo.LearningRecordFiles')) DROP INDEX [IX_LearningRecordFiles_Record] ON [dbo].[LearningRecordFiles];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningRecords_Review' AND object_id = OBJECT_ID('dbo.LearningRecords')) DROP INDEX [IX_LearningRecords_Review] ON [dbo].[LearningRecords];");

			foreach (var column in new[] { "ScanState", "EvidenceKind", "SizeBytes" })
				Execute.Sql($@"
IF OBJECT_ID('DF_LearningRecordFiles_{column}', 'D') IS NOT NULL ALTER TABLE [dbo].[LearningRecordFiles] DROP CONSTRAINT [DF_LearningRecordFiles_{column}];
IF COL_LENGTH('dbo.LearningRecordFiles', '{column}') IS NOT NULL ALTER TABLE [dbo].[LearningRecordFiles] DROP COLUMN [{column}];");
			foreach (var column in new[] { "Sha256", "LearningRecordId" })
				Execute.Sql($"IF COL_LENGTH('dbo.LearningRecordFiles', '{column}') IS NOT NULL ALTER TABLE [dbo].[LearningRecordFiles] DROP COLUMN [{column}];");

			foreach (var column in new[] { "SubmittedOn", "ReviewState" })
				Execute.Sql($"IF COL_LENGTH('dbo.LearningRecords', '{column}') IS NOT NULL ALTER TABLE [dbo].[LearningRecords] DROP COLUMN [{column}];");
		}
	}
}
