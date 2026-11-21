using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Intake spam screening (call-intake-and-ai-destinations-plan.md §9, 2026-11-20):
	/// <list type="bullet">
	/// <item>IntakeBlockedSenders: senders a department blocked from its intake channels ("dismiss as spam"). At most one standing row
	/// (not lifted) per department and sender; expiry is checked when a message arrives.</item>
	/// <item>IntakeMessages.Screening / ScreenedOn / ScreenedByUserId: why a pending intake call is badged and held out of the
	/// dispatcher desk's offers (Enhanced AI "likely not an incident", repeat sender, dismissed as spam).</item>
	/// <item>IX_IntakeMessages_Sender: the per-sender hourly limit reads a department's recent calls from one sender.</item>
	/// </list>
	/// No foreign keys; the new table is purged with the department (UnlinkedDepartmentCleanup).
	/// </summary>
	[Migration(326)]
	public class M0326_AddIntakeSpamScreening : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IntakeBlockedSenders").Exists())
			{
				Create.Table("IntakeBlockedSenders")
					.WithColumn("IntakeBlockedSenderId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Sender").AsString(256).NotNullable()
					.WithColumn("ChannelType").AsInt32().NotNullable()
					.WithColumn("Reason").AsString(100).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("BlockedByUserId").AsString(128).Nullable()
					.WithColumn("BlockedOn").AsDateTime2().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("RemovedByUserId").AsString(128).Nullable()
					.WithColumn("RemovedOn").AsDateTime2().Nullable();
			}

			Index("UX_IntakeBlockedSenders_Standing", "IntakeBlockedSenders",
				"CREATE UNIQUE INDEX [UX_IntakeBlockedSenders_Standing] ON [IntakeBlockedSenders] ([DepartmentId], [Sender]) WHERE [RemovedOn] IS NULL;");
			Index("IX_IntakeBlockedSenders_Ended", "IntakeBlockedSenders",
				"CREATE INDEX [IX_IntakeBlockedSenders_Ended] ON [IntakeBlockedSenders] ([RemovedOn], [ExpiresOn]);");

			if (Schema.Table("IntakeMessages").Exists() && !Schema.Table("IntakeMessages").Column("Screening").Exists())
				Alter.Table("IntakeMessages").AddColumn("Screening").AsInt32().Nullable();
			if (Schema.Table("IntakeMessages").Exists() && !Schema.Table("IntakeMessages").Column("ScreenedOn").Exists())
				Alter.Table("IntakeMessages").AddColumn("ScreenedOn").AsDateTime2().Nullable();
			if (Schema.Table("IntakeMessages").Exists() && !Schema.Table("IntakeMessages").Column("ScreenedByUserId").Exists())
				Alter.Table("IntakeMessages").AddColumn("ScreenedByUserId").AsString(128).Nullable();

			Index("IX_IntakeMessages_Sender", "IntakeMessages",
				"CREATE INDEX [IX_IntakeMessages_Sender] ON [IntakeMessages] ([DepartmentId], [Sender], [ReceivedOn]);");
		}

		public override void Down()
		{
			if (Schema.Table("IntakeBlockedSenders").Exists())
				Delete.Table("IntakeBlockedSenders");

			if (Schema.Table("IntakeMessages").Exists())
			{
				Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntakeMessages_Sender' AND object_id = OBJECT_ID('IntakeMessages')) " +
					"DROP INDEX [IX_IntakeMessages_Sender] ON [IntakeMessages];");
				foreach (var column in new[] { "ScreenedByUserId", "ScreenedOn", "Screening" })
				{
					if (Schema.Table("IntakeMessages").Column(column).Exists())
						Delete.Column(column).FromTable("IntakeMessages");
				}
			}
		}

		private void Index(string name, string table, string createSql)
		{
			Execute.Sql($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('{table}')) " + createSql);
		}
	}
}
