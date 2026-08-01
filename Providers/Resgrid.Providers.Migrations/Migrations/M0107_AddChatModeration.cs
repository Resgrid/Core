using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Chat moderation and administration tables: ChatMessageFlags (user reports of messages for
	/// moderator review), ChatModerationActions (immutable audit records of delete/mute/ban/lock/pin/
	/// flag-resolve/export actions), ChatDepartmentSettings (per-department retention policy and content
	/// toggles, one row per department) and ChatExports (queued transcript export jobs with the result
	/// stored as a blob).
	/// </summary>
	[Migration(107)]
	public class M0107_AddChatModeration : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessageFlags").Exists())
			{
				Create.Table("ChatMessageFlags")
					.WithColumn("ChatMessageFlagId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("FlaggedByUserId").AsString(450).Nullable()
					.WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Note").AsString(int.MaxValue).Nullable()
					.WithColumn("FlaggedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReviewedByUserId").AsString(450).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("ResolutionNote").AsString(int.MaxValue).Nullable();

				Create.Index("IX_ChatMessageFlags_Department_Status_FlaggedOn")
					.OnTable("ChatMessageFlags")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("Status").Ascending()
					.OnColumn("FlaggedOn").Ascending();
			}

			if (!Schema.Table("ChatModerationActions").Exists())
			{
				Create.Table("ChatModerationActions")
					.WithColumn("ChatModerationActionId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ChatChannelId").AsString(128).Nullable()
					.WithColumn("ChatMessageId").AsString(128).Nullable()
					.WithColumn("TargetUserId").AsString(450).Nullable()
					.WithColumn("TargetUnitId").AsInt32().Nullable()
					.WithColumn("ActionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PerformedByUserId").AsString(450).Nullable()
					.WithColumn("PerformedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Reason").AsString(int.MaxValue).Nullable()
					.WithColumn("DetailsJson").AsString(int.MaxValue).Nullable();

				Create.Index("IX_ChatModerationActions_Department_PerformedOn")
					.OnTable("ChatModerationActions")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("PerformedOn").Ascending();

				Create.Index("IX_ChatModerationActions_Channel")
					.OnTable("ChatModerationActions")
					.OnColumn("ChatChannelId").Ascending();
			}

			if (!Schema.Table("ChatDepartmentSettings").Exists())
			{
				Create.Table("ChatDepartmentSettings")
					.WithColumn("ChatDepartmentSettingId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RetentionDays").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AllowImages").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("AllowGifs").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("AllowLocationSharing").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("UrgentOverridesMute").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("MaxAttachmentSizeMb").AsInt32().NotNullable().WithDefaultValue(10)
					.WithColumn("ChatbotEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				// One settings row per department.
				Create.Index("UX_ChatDepartmentSettings_Department")
					.OnTable("ChatDepartmentSettings")
					.OnColumn("DepartmentId").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table("ChatExports").Exists())
			{
				Create.Table("ChatExports")
					.WithColumn("ChatExportId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RequestedByUserId").AsString(450).Nullable()
					.WithColumn("RequestedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ChatChannelId").AsString(128).Nullable()
					.WithColumn("StartDate").AsDateTime2().Nullable()
					.WithColumn("EndDate").AsDateTime2().Nullable()
					.WithColumn("Format").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("Error").AsString(int.MaxValue).Nullable();

				Create.Index("IX_ChatExports_Department_RequestedOn")
					.OnTable("ChatExports")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("RequestedOn").Ascending();

				Create.Index("IX_ChatExports_Status")
					.OnTable("ChatExports")
					.OnColumn("Status").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatExports").Exists())
				Delete.Table("ChatExports");

			if (Schema.Table("ChatDepartmentSettings").Exists())
				Delete.Table("ChatDepartmentSettings");

			if (Schema.Table("ChatModerationActions").Exists())
				Delete.Table("ChatModerationActions");

			if (Schema.Table("ChatMessageFlags").Exists())
				Delete.Table("ChatMessageFlags");
		}
	}
}
