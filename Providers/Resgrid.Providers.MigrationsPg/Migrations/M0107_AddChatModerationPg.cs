using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Chat moderation and administration tables: ChatMessageFlags (user reports of messages for
	/// moderator review), ChatModerationActions (immutable audit records of delete/mute/ban/lock/pin/
	/// flag-resolve/export actions), ChatDepartmentSettings (per-department retention policy and content
	/// toggles, one row per department) and ChatExports (queued transcript export jobs with the result
	/// stored as a blob).
	/// </summary>
	[Migration(107)]
	public class M0107_AddChatModerationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessageFlags".ToLower()).Exists())
			{
				Create.Table("ChatMessageFlags".ToLower())
					.WithColumn("ChatMessageFlagId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("FlaggedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Reason".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Note".ToLower()).AsCustom("text").Nullable()
					.WithColumn("FlaggedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReviewedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ReviewedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("ResolutionNote".ToLower()).AsCustom("text").Nullable();

				Create.Index("IX_ChatMessageFlags_Department_Status_FlaggedOn".ToLower())
					.OnTable("ChatMessageFlags".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("Status".ToLower()).Ascending()
					.OnColumn("FlaggedOn".ToLower()).Ascending();
			}

			if (!Schema.Table("ChatModerationActions".ToLower()).Exists())
			{
				Create.Table("ChatModerationActions".ToLower())
					.WithColumn("ChatModerationActionId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("TargetUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("TargetUnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("ActionType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PerformedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("PerformedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("Reason".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("DetailsJson".ToLower()).AsCustom("text").Nullable();

				Create.Index("IX_ChatModerationActions_Department_PerformedOn".ToLower())
					.OnTable("ChatModerationActions".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("PerformedOn".ToLower()).Ascending();

				Create.Index("IX_ChatModerationActions_Channel".ToLower())
					.OnTable("ChatModerationActions".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending();
			}

			if (!Schema.Table("ChatDepartmentSettings".ToLower()).Exists())
			{
				Create.Table("ChatDepartmentSettings".ToLower())
					.WithColumn("ChatDepartmentSettingId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RetentionDays".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AllowImages".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("AllowGifs".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("AllowLocationSharing".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("UrgentOverridesMute".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("MaxAttachmentSizeMb".ToLower()).AsInt32().NotNullable().WithDefaultValue(10)
					.WithColumn("ChatbotEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("ModifiedOn".ToLower()).AsDateTime2().Nullable();

				// One settings row per department.
				Create.Index("UX_ChatDepartmentSettings_Department".ToLower())
					.OnTable("ChatDepartmentSettings".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table("ChatExports".ToLower()).Exists())
			{
				Create.Table("ChatExports".ToLower())
					.WithColumn("ChatExportId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RequestedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RequestedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("StartDate".ToLower()).AsDateTime2().Nullable()
					.WithColumn("EndDate".ToLower()).AsDateTime2().Nullable()
					.WithColumn("Format".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Status".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CompletedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("Data".ToLower()).AsCustom("bytea").Nullable()
					.WithColumn("Error".ToLower()).AsCustom("text").Nullable();

				Create.Index("IX_ChatExports_Department_RequestedOn".ToLower())
					.OnTable("ChatExports".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("RequestedOn".ToLower()).Ascending();

				Create.Index("IX_ChatExports_Status".ToLower())
					.OnTable("ChatExports".ToLower())
					.OnColumn("Status".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatExports".ToLower()).Exists())
				Delete.Table("ChatExports".ToLower());

			if (Schema.Table("ChatDepartmentSettings".ToLower()).Exists())
				Delete.Table("ChatDepartmentSettings".ToLower());

			if (Schema.Table("ChatModerationActions".ToLower()).Exists())
				Delete.Table("ChatModerationActions".ToLower());

			if (Schema.Table("ChatMessageFlags".ToLower()).Exists())
				Delete.Table("ChatMessageFlags".ToLower());
		}
	}
}
