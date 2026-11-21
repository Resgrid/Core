using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Intake text conversations and dedicated-number release warnings (call-intake-and-ai-destinations-plan.md §8, 2026-11-20).
	/// M0324 is the dispatcher desk's (another feature), so this addendum to M0323 takes M0325.
	/// <list type="bullet">
	/// <item>IntakeConversations: one SMS thread per texter and department, opened by a text to the department's dedicated number and
	/// continued on the master Resgrid number (the dedicated number is inbound-only, D5). Who answers: Enhanced AI, nobody yet, or a
	/// connected dispatcher (desk or call screen).</item>
	/// <item>IntakeConversationMessages: delivery log per message (direction, author, provider id, status). The text itself is a call
	/// note on the conversation's pending call (<c>CallNoteId</c>), so ADP departments keep it protected like any other note.</item>
	/// <item>DepartmentPhoneNumbers.ReleaseWarningsSent / LastReleaseWarningOn: which admin warnings before release went out.</item>
	/// </list>
	/// No foreign keys; both tables are purged with the department (UnlinkedDepartmentCleanup).
	/// </summary>
	[Migration(325)]
	public class M0325_AddIntakeConversations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IntakeConversations").Exists())
			{
				Create.Table("IntakeConversations")
					.WithColumn("IntakeConversationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("IntakeChannelId").AsString(36).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("ChannelType").AsInt32().NotNullable()
					.WithColumn("ParticipantAddress").AsString(256).NotNullable()
					.WithColumn("ParticipantName").AsString(200).Nullable()
					.WithColumn("InboundAddress").AsString(256).Nullable()
					.WithColumn("ReplyFromAddress").AsString(256).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("Handler").AsInt32().NotNullable()
					.WithColumn("AssignedUserId").AsString(128).Nullable()
					.WithColumn("AssignedOn").AsDateTime2().Nullable()
					.WithColumn("AssistantTurns").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OptedOut").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DeskItemId").AsString(36).Nullable()
					.WithColumn("StartedOn").AsDateTime2().NotNullable()
					.WithColumn("LastInboundOn").AsDateTime2().Nullable()
					.WithColumn("LastActivityOn").AsDateTime2().NotNullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("CloseReason").AsString(200).Nullable();
			}

			// Replies to the shared master number find the texter's open thread; at most one open thread per texter and department.
			Index("IX_IntakeConversations_Participant", "IntakeConversations",
				"CREATE INDEX [IX_IntakeConversations_Participant] ON [IntakeConversations] ([ChannelType], [ParticipantAddress], [State], [LastActivityOn]);");
			Index("UX_IntakeConversations_Open", "IntakeConversations",
				"CREATE UNIQUE INDEX [UX_IntakeConversations_Open] ON [IntakeConversations] ([DepartmentId], [ChannelType], [ParticipantAddress]) WHERE [State] = 1;");
			Index("IX_IntakeConversations_Call", "IntakeConversations",
				"CREATE INDEX [IX_IntakeConversations_Call] ON [IntakeConversations] ([CallId]) WHERE [CallId] IS NOT NULL;");
			Index("IX_IntakeConversations_Activity", "IntakeConversations",
				"CREATE INDEX [IX_IntakeConversations_Activity] ON [IntakeConversations] ([State], [LastActivityOn]);");

			if (!Schema.Table("IntakeConversationMessages").Exists())
			{
				Create.Table("IntakeConversationMessages")
					.WithColumn("IntakeConversationMessageId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("IntakeConversationId").AsString(36).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Direction").AsInt32().NotNullable()
					.WithColumn("Author").AsInt32().NotNullable()
					.WithColumn("AuthorUserId").AsString(128).Nullable()
					.WithColumn("CallNoteId").AsInt32().Nullable()
					.WithColumn("ProviderMessageId").AsString(256).Nullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("Error").AsString(1000).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("SentOn").AsDateTime2().Nullable();
			}

			Index("IX_IntakeConversationMessages_Conversation", "IntakeConversationMessages",
				"CREATE INDEX [IX_IntakeConversationMessages_Conversation] ON [IntakeConversationMessages] ([IntakeConversationId], [CreatedOn]);");
			// A provider retry of the same inbound text is one message.
			Index("UX_IntakeConversationMessages_Provider", "IntakeConversationMessages",
				"CREATE UNIQUE INDEX [UX_IntakeConversationMessages_Provider] ON [IntakeConversationMessages] ([IntakeConversationId], [ProviderMessageId]) WHERE [ProviderMessageId] IS NOT NULL AND [Direction] = 1;");

			if (Schema.Table("DepartmentPhoneNumbers").Exists() && !Schema.Table("DepartmentPhoneNumbers").Column("ReleaseWarningsSent").Exists())
				Alter.Table("DepartmentPhoneNumbers").AddColumn("ReleaseWarningsSent").AsInt32().NotNullable().WithDefaultValue(0);
			if (Schema.Table("DepartmentPhoneNumbers").Exists() && !Schema.Table("DepartmentPhoneNumbers").Column("LastReleaseWarningOn").Exists())
				Alter.Table("DepartmentPhoneNumbers").AddColumn("LastReleaseWarningOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			foreach (var table in new[] { "IntakeConversationMessages", "IntakeConversations" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}

			if (Schema.Table("DepartmentPhoneNumbers").Exists() && Schema.Table("DepartmentPhoneNumbers").Column("LastReleaseWarningOn").Exists())
				Delete.Column("LastReleaseWarningOn").FromTable("DepartmentPhoneNumbers");
			if (Schema.Table("DepartmentPhoneNumbers").Exists() && Schema.Table("DepartmentPhoneNumbers").Column("ReleaseWarningsSent").Exists())
				Execute.Sql("DECLARE @c sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c ON c.default_object_id = d.object_id " +
					"WHERE d.parent_object_id = OBJECT_ID('DepartmentPhoneNumbers') AND c.name = 'ReleaseWarningsSent'); " +
					"IF @c IS NOT NULL EXEC('ALTER TABLE [DepartmentPhoneNumbers] DROP CONSTRAINT [' + @c + ']'); " +
					"ALTER TABLE [DepartmentPhoneNumbers] DROP COLUMN [ReleaseWarningsSent];");
		}

		private void Index(string name, string table, string createSql)
		{
			Execute.Sql($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('{table}')) " + createSql);
		}
	}
}
