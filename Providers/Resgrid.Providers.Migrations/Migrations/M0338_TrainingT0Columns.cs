using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T0 (protocols-training-knowledge-plan §13 T0): the columns the fixed and exposed baseline training needs.
	/// - Trainings: self-enrol, scheduled start, relative due days, closed to new assignees, one simple repeat, attempt limit,
	///   one question per page, reminder lead days / overdue notices / custom message, hours and CE fields, a category id,
	///   archive and update stamps. CategoryId carries no foreign key: T1 adds TrainingCategories and the key with it.
	/// - TrainingQuestions.Type: single, multiple (all-or-nothing) or true/false (R-S9).
	/// - TrainingUsers: Passed apart from Complete (T-D2), AssignedOn/DueOn (R-T8), the assignment source, per-assignee notice
	///   stamps (T-D19), removal (T-D15), extension and exemption (R-C2), attempt count and repeat cycle.
	/// Backfills (idempotent): duplicate assignees are removed keeping the most complete row, then a unique index on
	/// (TrainingId, UserId) (T-D14); Passed is set once from the stored score for completed quiz rows (the last time pass/fail
	/// is derived from the current quiz, T-D20) and a failed row is no longer Complete; AssignedOn falls back to the
	/// training's creation and NotifiedOn to the training's Notified stamp, so no old assignment is announced again. Existing
	/// trainings keep SendOverdueNotices off. Guarded for safe retry.
	/// </summary>
	[Migration(338)]
	public class M0338_TrainingT0Columns : Migration
	{
		private static readonly string[][] TrainingColumns =
		{
			new[] { "AllowSelfEnroll", "bit NOT NULL CONSTRAINT [DF_Trainings_AllowSelfEnroll] DEFAULT 0" },
			new[] { "StartsOn", "datetime2 NULL" },
			new[] { "DueDays", "int NULL" },
			new[] { "ClosedToNewAssignees", "bit NOT NULL CONSTRAINT [DF_Trainings_ClosedToNewAssignees] DEFAULT 0" },
			new[] { "RepeatMonths", "int NULL" },
			new[] { "NextRepeatOn", "datetime2 NULL" },
			new[] { "QuizAttemptLimit", "int NULL" },
			new[] { "OneQuestionPerPage", "bit NOT NULL CONSTRAINT [DF_Trainings_OneQuestionPerPage] DEFAULT 0" },
			new[] { "ReminderLeadDaysCsv", "nvarchar(100) NULL" },
			new[] { "SendOverdueNotices", "bit NOT NULL CONSTRAINT [DF_Trainings_SendOverdueNotices] DEFAULT 0" },
			new[] { "OverdueRepeatDays", "int NULL" },
			new[] { "ReminderMessage", "nvarchar(500) NULL" },
			new[] { "Hours", "decimal(9,2) NULL" },
			new[] { "CeHours", "decimal(9,2) NULL" },
			new[] { "CeProvider", "nvarchar(250) NULL" },
			new[] { "CeCourseNumber", "nvarchar(100) NULL" },
			new[] { "CategoryId", "int NULL" },
			new[] { "ArchivedOn", "datetime2 NULL" },
			new[] { "ArchivedByUserId", "nvarchar(128) NULL" },
			new[] { "UpdatedOn", "datetime2 NULL" },
			new[] { "UpdatedByUserId", "nvarchar(128) NULL" }
		};

		private static readonly string[][] TrainingUserColumns =
		{
			new[] { "Passed", "bit NULL" },
			new[] { "AssignedOn", "datetime2 NULL" },
			new[] { "DueOn", "datetime2 NULL" },
			new[] { "AssignmentSource", "int NOT NULL CONSTRAINT [DF_TrainingUsers_AssignmentSource] DEFAULT 0" },
			new[] { "NotifiedOn", "datetime2 NULL" },
			new[] { "RemindedOn", "datetime2 NULL" },
			new[] { "LastReminderLeadDays", "int NULL" },
			new[] { "OverdueNotifiedOn", "datetime2 NULL" },
			new[] { "RemovedOn", "datetime2 NULL" },
			new[] { "RemovedByUserId", "nvarchar(128) NULL" },
			new[] { "OriginalDueOn", "datetime2 NULL" },
			new[] { "ExtensionReason", "nvarchar(500) NULL" },
			new[] { "ExtendedByUserId", "nvarchar(128) NULL" },
			new[] { "ExtendedOn", "datetime2 NULL" },
			new[] { "ExemptOn", "datetime2 NULL" },
			new[] { "ExemptReason", "nvarchar(500) NULL" },
			new[] { "ExemptByUserId", "nvarchar(128) NULL" },
			new[] { "ExemptUntil", "datetime2 NULL" },
			new[] { "AttemptCount", "int NOT NULL CONSTRAINT [DF_TrainingUsers_AttemptCount] DEFAULT 0" },
			new[] { "Cycle", "int NOT NULL CONSTRAINT [DF_TrainingUsers_Cycle] DEFAULT 0" }
		};

		public override void Up()
		{
			foreach (var column in TrainingColumns)
				Execute.Sql($"IF COL_LENGTH('Trainings', '{column[0]}') IS NULL ALTER TABLE [Trainings] ADD [{column[0]}] {column[1]};");

			foreach (var column in TrainingUserColumns)
				Execute.Sql($"IF COL_LENGTH('TrainingUsers', '{column[0]}') IS NULL ALTER TABLE [TrainingUsers] ADD [{column[0]}] {column[1]};");

			Execute.Sql("IF COL_LENGTH('TrainingQuestions', 'Type') IS NULL ALTER TABLE [TrainingQuestions] ADD [Type] int NOT NULL CONSTRAINT [DF_TrainingQuestions_Type] DEFAULT 0;");

			// T-D14: one row per member and training. The most complete row survives (complete, passed, viewed, best score, the
			// earliest completion as the evidence), then the lowest id.
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrainingUsers_TrainingId_UserId' AND object_id = OBJECT_ID('TrainingUsers'))
BEGIN
	;WITH ranked AS (
		SELECT [TrainingUserId], ROW_NUMBER() OVER (PARTITION BY [TrainingId], [UserId]
			ORDER BY [Complete] DESC, CASE WHEN [Passed] = 1 THEN 1 ELSE 0 END DESC, [Viewed] DESC, [Score] DESC,
				CASE WHEN [CompletedOn] IS NULL THEN 1 ELSE 0 END, [CompletedOn] ASC, [TrainingUserId] ASC) AS rn
		FROM [TrainingUsers])
	DELETE FROM [TrainingUsers] WHERE [TrainingUserId] IN (SELECT [TrainingUserId] FROM ranked WHERE rn > 1);
END");

			// T-D2: the last derivation of pass/fail from the current quiz; from now on it is stored when the quiz is taken.
			Execute.Sql(@"
UPDATE tu SET tu.[Passed] = CASE WHEN tu.[Score] >= t.[MinimumScore] THEN 1 ELSE 0 END
FROM [TrainingUsers] tu JOIN [Trainings] t ON t.[TrainingId] = tu.[TrainingId]
WHERE tu.[Passed] IS NULL AND tu.[Complete] = 1 AND EXISTS (SELECT 1 FROM [TrainingQuestions] q WHERE q.[TrainingId] = t.[TrainingId]);");

			// A failed quiz is not a completion any more: it stays outstanding with its score and Passed = 0.
			Execute.Sql("UPDATE [TrainingUsers] SET [Complete] = 0, [CompletedOn] = NULL WHERE [Passed] = 0 AND [Complete] = 1;");

			Execute.Sql(@"
UPDATE tu SET tu.[AssignedOn] = t.[CreatedOn]
FROM [TrainingUsers] tu JOIN [Trainings] t ON t.[TrainingId] = tu.[TrainingId]
WHERE tu.[AssignedOn] IS NULL;");

			// Per-assignee notice stamps start from the training's: an assignment of an announced training is not announced again.
			Execute.Sql(@"
UPDATE tu SET tu.[NotifiedOn] = t.[Notified]
FROM [TrainingUsers] tu JOIN [Trainings] t ON t.[TrainingId] = tu.[TrainingId]
WHERE tu.[NotifiedOn] IS NULL AND t.[Notified] IS NOT NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrainingUsers_TrainingId_UserId' AND object_id = OBJECT_ID('TrainingUsers')) CREATE UNIQUE INDEX [UX_TrainingUsers_TrainingId_UserId] ON [TrainingUsers] ([TrainingId], [UserId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrainingUsers_Complete_DueOn' AND object_id = OBJECT_ID('TrainingUsers')) CREATE INDEX [IX_TrainingUsers_Complete_DueOn] ON [TrainingUsers] ([Complete], [DueOn]) INCLUDE ([TrainingId], [NotifiedOn]);");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrainingUsers_Complete_DueOn' AND object_id = OBJECT_ID('TrainingUsers')) DROP INDEX [IX_TrainingUsers_Complete_DueOn] ON [TrainingUsers];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrainingUsers_TrainingId_UserId' AND object_id = OBJECT_ID('TrainingUsers')) DROP INDEX [UX_TrainingUsers_TrainingId_UserId] ON [TrainingUsers];");

			// A failed row was Complete before Up; put it back so the old pages read it as they did.
			Execute.Sql("IF COL_LENGTH('TrainingUsers', 'Passed') IS NOT NULL EXEC('UPDATE [TrainingUsers] SET [Complete] = 1 WHERE [Passed] = 0 AND [Complete] = 0');");

			DropColumn("TrainingQuestions", "Type");
			foreach (var column in TrainingUserColumns)
				DropColumn("TrainingUsers", column[0]);
			foreach (var column in TrainingColumns)
				DropColumn("Trainings", column[0]);
		}

		private void DropColumn(string table, string column)
		{
			// SQL Server keeps a column's default as its own constraint; it must go before the column.
			Execute.Sql($@"IF COL_LENGTH('{table}', '{column}') IS NOT NULL
BEGIN
	DECLARE @constraint sysname;
	SELECT @constraint = dc.name FROM sys.default_constraints dc
	JOIN sys.columns c ON c.default_object_id = dc.object_id
	WHERE dc.parent_object_id = OBJECT_ID('dbo.{table}') AND c.name = '{column}';
	IF @constraint IS NOT NULL EXEC('ALTER TABLE dbo.[{table}] DROP CONSTRAINT [' + @constraint + ']');
	ALTER TABLE [{table}] DROP COLUMN [{column}];
END");
		}
	}
}
