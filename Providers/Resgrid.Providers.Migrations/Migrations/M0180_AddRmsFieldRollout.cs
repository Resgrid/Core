using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Field Records media policy and rollout telemetry (RMS plan RMS-1D, registry M0180): the per-attachment
	/// record of whether photo coordinates survived upload, and the bounded per-app rollout event stream the
	/// adoption dashboards read. Events carry identifiers, counts and outcome codes only — never record content.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(180)]
	public class M0180_AddRmsFieldRollout : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsRecordAttachments").Exists() && !Schema.Table("RmsRecordAttachments").Column("MediaLocationRetained").Exists())
				Alter.Table("RmsRecordAttachments").AddColumn("MediaLocationRetained").AsBoolean().NotNullable().WithDefaultValue(false);

			if (!Schema.Table("RmsFieldRolloutEvents").Exists())
			{
				Create.Table("RmsFieldRolloutEvents")
					.WithColumn("RmsFieldRolloutEventId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("OriginClient").AsInt32().NotNullable()
					.WithColumn("AppVersion").AsString(32).Nullable()
					.WithColumn("ClientCapability").AsString(32).Nullable()
					.WithColumn("EventType").AsString(32).NotNullable()
					.WithColumn("Outcome").AsString(48).NotNullable()
					.WithColumn("DefinitionKey").AsString(64).Nullable()
					.WithColumn("DefinitionVersion").AsInt32().Nullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("DurationMs").AsInt64().Nullable()
					.WithColumn("ItemCount").AsInt32().Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("RecordedOn").AsDateTime2().NotNullable();

				Create.Index("IX_RmsFieldRolloutEvents_Window").OnTable("RmsFieldRolloutEvents")
					.OnColumn("DepartmentId").Ascending().OnColumn("OccurredOn").Ascending();
				Create.Index("IX_RmsFieldRolloutEvents_App").OnTable("RmsFieldRolloutEvents")
					.OnColumn("DepartmentId").Ascending().OnColumn("OriginClient").Ascending().OnColumn("EventType").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsFieldRolloutEvents").Exists())
				Delete.Table("RmsFieldRolloutEvents");
			if (Schema.Table("RmsRecordAttachments").Exists() && Schema.Table("RmsRecordAttachments").Column("MediaLocationRetained").Exists())
				Delete.Column("MediaLocationRetained").FromTable("RmsRecordAttachments");
		}
	}
}
