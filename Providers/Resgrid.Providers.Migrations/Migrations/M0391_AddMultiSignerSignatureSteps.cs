using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S3: multi-signer ordered requests. SignatureRequestSigners gains the step shape — SignerKind (0 external, every
	/// S2 row; 1 member), RequiredLevel (L1/L2 member, L4 external; 0 on S2 rows), EligibilityJson ("any eligible member" by
	/// certification type or role, evaluator mode), StepLabel, DeclineReason, TurnStartedOn and the worker 90 reminder columns
	/// (NextReminderOn, LastRemindedOn, ReminderCount) — plus an index for a member's pending steps and one for due reminders.
	/// Guarded for safe retry. Down first removes the requests that hold member steps (the S2 shape cannot tell a member step from
	/// an external signer; their SignatureRecords stay), then the indexes, the default constraints and the columns.
	/// </summary>
	[Migration(391)]
	public class M0391_AddMultiSignerSignatureSteps : Migration
	{
		private static readonly (string Column, string Definition)[] Columns =
		{
			("SignerKind", "int NOT NULL CONSTRAINT [DF_SignatureRequestSigners_SignerKind] DEFAULT 0"),
			("RequiredLevel", "int NOT NULL CONSTRAINT [DF_SignatureRequestSigners_RequiredLevel] DEFAULT 0"),
			("EligibilityJson", "nvarchar(1000) NULL"),
			("StepLabel", "nvarchar(200) NULL"),
			("DeclineReason", "nvarchar(1000) NULL"),
			("TurnStartedOn", "datetime2 NULL"),
			("NextReminderOn", "datetime2 NULL"),
			("LastRemindedOn", "datetime2 NULL"),
			("ReminderCount", "int NOT NULL CONSTRAINT [DF_SignatureRequestSigners_ReminderCount] DEFAULT 0")
		};

		private static readonly string[] Defaults = { "SignerKind", "RequiredLevel", "ReminderCount" };

		public override void Up()
		{
			foreach (var (column, definition) in Columns)
				Execute.Sql($"IF COL_LENGTH('dbo.SignatureRequestSigners', '{column}') IS NULL ALTER TABLE [dbo].[SignatureRequestSigners] ADD [{column}] {definition};");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequestSigners_Member' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) " +
				"CREATE INDEX [IX_SignatureRequestSigners_Member] ON [dbo].[SignatureRequestSigners] ([DepartmentId], [UserId], [Status]) INCLUDE ([SignatureRequestId], [SignerKind]) WHERE [UserId] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequestSigners_Reminder' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) " +
				"CREATE INDEX [IX_SignatureRequestSigners_Reminder] ON [dbo].[SignatureRequestSigners] ([NextReminderOn]) WHERE [NextReminderOn] IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql(@"
IF COL_LENGTH('dbo.SignatureRequestSigners', 'SignerKind') IS NOT NULL
BEGIN
	EXEC('DELETE c FROM [dbo].[SignatureCaptures] c WHERE EXISTS (SELECT 1 FROM [dbo].[SignatureRequestSigners] s WHERE s.[SignatureRequestId] = c.[SignatureRequestId] AND s.[SignerKind] = 1);
	DELETE r FROM [dbo].[SignatureRequests] r WHERE EXISTS (SELECT 1 FROM [dbo].[SignatureRequestSigners] s WHERE s.[SignatureRequestId] = r.[SignatureRequestId] AND s.[SignerKind] = 1);
	DELETE s FROM [dbo].[SignatureRequestSigners] s WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SignatureRequests] r WHERE r.[SignatureRequestId] = s.[SignatureRequestId]);');
END");

			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequestSigners_Reminder' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) DROP INDEX [IX_SignatureRequestSigners_Reminder] ON [dbo].[SignatureRequestSigners];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SignatureRequestSigners_Member' AND object_id = OBJECT_ID('dbo.SignatureRequestSigners')) DROP INDEX [IX_SignatureRequestSigners_Member] ON [dbo].[SignatureRequestSigners];");

			foreach (var column in Defaults)
				Execute.Sql($"IF OBJECT_ID('dbo.DF_SignatureRequestSigners_{column}', 'D') IS NOT NULL ALTER TABLE [dbo].[SignatureRequestSigners] DROP CONSTRAINT [DF_SignatureRequestSigners_{column}];");

			foreach (var (column, _) in Columns)
				Execute.Sql($"IF COL_LENGTH('dbo.SignatureRequestSigners', '{column}') IS NOT NULL ALTER TABLE [dbo].[SignatureRequestSigners] DROP COLUMN [{column}];");
		}
	}
}
