using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Voice intake call recording on the Private Inbound Dispatch Number (2026-11-23):
	/// <list type="bullet">
	/// <item>DepartmentPhoneNumbers.VoiceRecordingEnabled (bit, default 0): the number records its voice intake calls. The caller is told
	/// at the press-1 gate and the recording starts only after they press 1.</item>
	/// <item>VoiceIntakeSessions.Recording*: where the call's recording is (state, carrier sid, duration, attach attempts and lease, the
	/// call file it became, when the carrier copy was deleted). Purged with the session (worker 80).</item>
	/// <item>IX_VoiceIntakeSessions_Recording: worker 80 reads recordings with work left.</item>
	/// </list>
	/// No foreign keys.
	/// </summary>
	[Migration(332)]
	public class M0332_AddVoiceIntakeRecording : Migration
	{
		public override void Up()
		{
			if (Schema.Table("DepartmentPhoneNumbers").Exists() && !Schema.Table("DepartmentPhoneNumbers").Column("VoiceRecordingEnabled").Exists())
				Alter.Table("DepartmentPhoneNumbers").AddColumn("VoiceRecordingEnabled").AsBoolean().NotNullable().WithDefaultValue(false);

			if (!Schema.Table("VoiceIntakeSessions").Exists())
				return;

			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingState").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingState").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingSid").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingSid").AsString(64).Nullable();
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingDurationSeconds").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingDurationSeconds").AsInt32().Nullable();
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingAttempts").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingAttempts").AsInt32().NotNullable().WithDefaultValue(0);
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingClaimedOn").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingClaimedOn").AsDateTime2().Nullable();
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingCallAttachmentId").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingCallAttachmentId").AsInt32().Nullable();
			if (!Schema.Table("VoiceIntakeSessions").Column("RecordingCarrierDeletedOn").Exists())
				Alter.Table("VoiceIntakeSessions").AddColumn("RecordingCarrierDeletedOn").AsDateTime2().Nullable();

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VoiceIntakeSessions_Recording' AND object_id = OBJECT_ID('VoiceIntakeSessions')) " +
				"CREATE INDEX [IX_VoiceIntakeSessions_Recording] ON [VoiceIntakeSessions] ([RecordingState], [StartedOn]);");
		}

		public override void Down()
		{
			if (Schema.Table("VoiceIntakeSessions").Exists())
			{
				Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VoiceIntakeSessions_Recording' AND object_id = OBJECT_ID('VoiceIntakeSessions')) " +
					"DROP INDEX [IX_VoiceIntakeSessions_Recording] ON [VoiceIntakeSessions];");

				foreach (var column in new[] { "RecordingCarrierDeletedOn", "RecordingCallAttachmentId", "RecordingClaimedOn", "RecordingAttempts",
					"RecordingDurationSeconds", "RecordingSid", "RecordingState" })
				{
					if (Schema.Table("VoiceIntakeSessions").Column(column).Exists())
						DropColumnWithDefault("VoiceIntakeSessions", column);
				}
			}

			if (Schema.Table("DepartmentPhoneNumbers").Exists() && Schema.Table("DepartmentPhoneNumbers").Column("VoiceRecordingEnabled").Exists())
				DropColumnWithDefault("DepartmentPhoneNumbers", "VoiceRecordingEnabled");
		}

		/// <summary>SQL Server refuses to drop a column a default constraint still names: drop the (generated) constraint first.</summary>
		private void DropColumnWithDefault(string table, string column)
		{
			Execute.Sql(
				"DECLARE @constraint sysname = (SELECT dc.name FROM sys.default_constraints dc " +
				"JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id " +
				$"WHERE dc.parent_object_id = OBJECT_ID('{table}') AND c.name = '{column}'); " +
				$"IF @constraint IS NOT NULL EXEC('ALTER TABLE [{table}] DROP CONSTRAINT [' + @constraint + ']');");
			Delete.Column(column).FromTable(table);
		}
	}
}
