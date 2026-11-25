using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P2, distribution and acknowledgment:
	/// <list type="bullet">
	/// <item>DispatchProtocols gains AckDueDays (null = 30 days), AckBundleOrder (the onboarding bundle's order), AckTrainingId (the
	/// baseline Training whose quiz gates acknowledgment) and StandardsTags (LMS proof-of-compliance mapping).</item>
	/// <item>ProtocolVersions.QuizTrainingId: the training frozen into the version it gates, at publish.</item>
	/// <item>ProtocolAcknowledgments: one row per member per version (unique), assigned on publish and on entering the audience,
	/// signed at L1 bound to the version hash (SignatureRecordId), with reminder and escalation stages for worker 86.</item>
	/// </list>
	/// ProtocolAcknowledgments references ProtocolVersions and DispatchProtocols without cascade; ProtocolDepartmentCleanup and the
	/// retention purge remove it before the versions. Guarded for safe retry.
	/// </summary>
	[Migration(354)]
	public class M0354_AddProtocolAcknowledgments : Migration
	{
		private static readonly (string Name, string Definition)[] ProtocolColumns =
		{
			("AckDueDays", "int NULL"),
			("AckBundleOrder", "int NOT NULL CONSTRAINT [DF_DispatchProtocols_AckBundleOrder] DEFAULT (0)"),
			("AckTrainingId", "int NULL"),
			("StandardsTags", "nvarchar(500) NULL")
		};

		public override void Up()
		{
			foreach (var (name, definition) in ProtocolColumns)
				Execute.Sql($"IF COL_LENGTH('dbo.DispatchProtocols', '{name}') IS NULL ALTER TABLE [dbo].[DispatchProtocols] ADD [{name}] {definition};");

			Execute.Sql("IF COL_LENGTH('dbo.ProtocolVersions', 'QuizTrainingId') IS NULL ALTER TABLE [dbo].[ProtocolVersions] ADD [QuizTrainingId] int NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolAcknowledgments', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[ProtocolAcknowledgments] (
		[ProtocolAcknowledgmentId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[DispatchProtocolId] int NOT NULL,
		[ProtocolVersionId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[Status] int NOT NULL,
		[Source] int NOT NULL,
		[AssignedOn] datetime2 NOT NULL,
		[DueOn] datetime2 NULL,
		[EnteredAudienceOn] datetime2 NULL,
		[BundleOrder] int NOT NULL CONSTRAINT [DF_ProtocolAcknowledgments_BundleOrder] DEFAULT (0),
		[OpenedOn] datetime2 NULL,
		[SignedOn] datetime2 NULL,
		[SignatureRecordId] int NULL,
		[CompletedOn] datetime2 NULL,
		[QuizTrainingId] int NULL,
		[CarriedFromAcknowledgmentId] int NULL,
		[WithdrawnOn] datetime2 NULL,
		[ReminderStage] int NOT NULL CONSTRAINT [DF_ProtocolAcknowledgments_ReminderStage] DEFAULT (0),
		[LastReminderOn] datetime2 NULL,
		[EscalatedOn] datetime2 NULL,
		[ViaOffline] bit NOT NULL CONSTRAINT [DF_ProtocolAcknowledgments_ViaOffline] DEFAULT (0),
		CONSTRAINT [PK_ProtocolAcknowledgments] PRIMARY KEY CLUSTERED ([ProtocolAcknowledgmentId] ASC),
		CONSTRAINT [FK_ProtocolAcknowledgments_ProtocolVersions] FOREIGN KEY ([ProtocolVersionId]) REFERENCES [dbo].[ProtocolVersions] ([ProtocolVersionId]),
		CONSTRAINT [FK_ProtocolAcknowledgments_DispatchProtocols] FOREIGN KEY ([DispatchProtocolId]) REFERENCES [dbo].[DispatchProtocols] ([DispatchProtocolId])
	);
	CREATE UNIQUE NONCLUSTERED INDEX [UX_ProtocolAcknowledgments_Version_User] ON [dbo].[ProtocolAcknowledgments] ([ProtocolVersionId], [UserId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolAcknowledgments_Department_User] ON [dbo].[ProtocolAcknowledgments] ([DepartmentId], [UserId]) INCLUDE ([Status], [ProtocolVersionId]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolAcknowledgments_Protocol] ON [dbo].[ProtocolAcknowledgments] ([DispatchProtocolId], [Status]);
	CREATE NONCLUSTERED INDEX [IX_ProtocolAcknowledgments_Status_DueOn] ON [dbo].[ProtocolAcknowledgments] ([Status], [DueOn]) INCLUDE ([DepartmentId], [ReminderStage]);
END");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolAcknowledgments', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolAcknowledgments];");
			Execute.Sql("IF COL_LENGTH('dbo.ProtocolVersions', 'QuizTrainingId') IS NOT NULL ALTER TABLE [dbo].[ProtocolVersions] DROP COLUMN [QuizTrainingId];");

			for (var i = ProtocolColumns.Length - 1; i >= 0; i--)
			{
				var name = ProtocolColumns[i].Name;
				Execute.Sql($@"
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DispatchProtocols_{name}' AND parent_object_id = OBJECT_ID('dbo.DispatchProtocols'))
	ALTER TABLE [dbo].[DispatchProtocols] DROP CONSTRAINT [DF_DispatchProtocols_{name}];
IF COL_LENGTH('dbo.DispatchProtocols', '{name}') IS NOT NULL ALTER TABLE [dbo].[DispatchProtocols] DROP COLUMN [{name}];");
			}
		}
	}
}
