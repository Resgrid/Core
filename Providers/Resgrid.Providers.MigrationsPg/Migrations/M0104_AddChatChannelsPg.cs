using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Chat system channel tables: ChatChannels (DM/group/department/incident/chatbot channels with the
	/// per-channel message sequence high-water mark), ChatChannelAccessRules (OR-evaluated access rules
	/// for CustomLocked channels) and ChatChannelMembers (per-participant read pointers, notification
	/// preferences and moderation state). Partial unique indexes enforce DM dedup, one lane channel per
	/// command node, one chatbot conversation per user, and one default channel per group/department.
	/// </summary>
	[Migration(104)]
	public class M0104_AddChatChannelsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatChannels".ToLower()).Exists())
			{
				Create.Table("ChatChannels".ToLower())
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("ChannelType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Topic".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("GroupId".ToLower()).AsInt32().Nullable()
					.WithColumn("CallId".ToLower()).AsInt32().Nullable()
					.WithColumn("IncidentCommandId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CommandStructureNodeId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("OwnerUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("DmKey".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IsArchived".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ArchivedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("IsLocked".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LockedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("LockedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("LastMessageSeq".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("LastMessageOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("RetentionOverrideDays".ToLower()).AsInt32().Nullable()
					.WithColumn("ModifiedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_ChatChannels_Department_Type".ToLower())
					.OnTable("ChatChannels".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("ChannelType".ToLower()).Ascending();

				Create.Index("IX_ChatChannels_CallId".ToLower())
					.OnTable("ChatChannels".ToLower())
					.OnColumn("CallId".ToLower()).Ascending();

				// DM dedup: at most one channel per normalized participant key per department.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannels_department_dmkey ON chatchannels (departmentid, dmkey) WHERE dmkey IS NOT NULL;");

				// At most one lane channel per command structure node.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannels_node ON chatchannels (commandstructurenodeid) WHERE commandstructurenodeid IS NOT NULL;");

				// At most one chatbot conversation per user per department (ChannelType 8 = Chatbot).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannels_department_owner_bot ON chatchannels (departmentid, owneruserid) WHERE channeltype = 8 AND owneruserid IS NOT NULL;");

				// At most one default channel per group (ChannelType 3 = GroupDefault).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannels_group_default ON chatchannels (groupid) WHERE channeltype = 3 AND groupid IS NOT NULL;");

				// At most one default channel per department (ChannelType 2 = DepartmentDefault).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannels_department_default ON chatchannels (departmentid) WHERE channeltype = 2;");
			}

			if (!Schema.Table("ChatChannelAccessRules".ToLower()).Exists())
			{
				Create.Table("ChatChannelAccessRules".ToLower())
					.WithColumn("ChatChannelAccessRuleId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RuleType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GroupId".ToLower()).AsInt32().Nullable()
					.WithColumn("PersonnelRoleId".ToLower()).AsInt32().Nullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AddedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatChannelAccessRules_Channel".ToLower())
					.OnTable("ChatChannelAccessRules".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending();
			}

			if (!Schema.Table("ChatChannelMembers".ToLower()).Exists())
			{
				Create.Table("ChatChannelMembers".ToLower())
					.WithColumn("ChatChannelMemberId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("ParticipantType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("DisplayNameOverride".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IsModerator".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("JoinedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RemovedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("LastReadSeq".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("LastReadOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("LastDeliveredSeq".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("MutedUntil".ToLower()).AsDateTime2().Nullable()
					.WithColumn("IsBanned".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("BannedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("BannedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("NotificationPreference".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ModifiedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_ChatChannelMembers_Channel".ToLower())
					.OnTable("ChatChannelMembers".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending();

				Create.Index("IX_ChatChannelMembers_Department_User".ToLower())
					.OnTable("ChatChannelMembers".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("UserId".ToLower()).Ascending();

				// At most one membership row per person per channel (ParticipantType 0 = User).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannelmembers_channel_user ON chatchannelmembers (chatchannelid, userid) WHERE participanttype = 0;");

				// At most one membership row per unit per channel (ParticipantType 1 = Unit).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatchannelmembers_channel_unit ON chatchannelmembers (chatchannelid, unitid) WHERE participanttype = 1;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatChannelMembers".ToLower()).Exists())
			{
				// Explicit index drops (the table drop would also remove them, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannelmembers_channel_user;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannelmembers_channel_unit;");

				Delete.Table("ChatChannelMembers".ToLower());
			}

			if (Schema.Table("ChatChannelAccessRules".ToLower()).Exists())
				Delete.Table("ChatChannelAccessRules".ToLower());

			if (Schema.Table("ChatChannels".ToLower()).Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannels_department_dmkey;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannels_node;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannels_department_owner_bot;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannels_group_default;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatchannels_department_default;");

				Delete.Table("ChatChannels".ToLower());
			}
		}
	}
}
