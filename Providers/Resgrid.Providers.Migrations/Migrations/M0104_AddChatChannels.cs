using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Chat system channel tables: ChatChannels (DM/group/department/incident/chatbot channels with the
	/// per-channel message sequence high-water mark), ChatChannelAccessRules (OR-evaluated access rules
	/// for CustomLocked channels) and ChatChannelMembers (per-participant read pointers, notification
	/// preferences and moderation state). Filtered unique indexes enforce DM dedup, one lane channel per
	/// command node, one chatbot conversation per user, and one default channel per group/department.
	/// </summary>
	[Migration(104)]
	public class M0104_AddChatChannels : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatChannels").Exists())
			{
				Create.Table("ChatChannels")
					.WithColumn("ChatChannelId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ChannelType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("Topic").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("GroupId").AsInt32().Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("IncidentCommandId").AsString(128).Nullable()
					.WithColumn("CommandStructureNodeId").AsString(128).Nullable()
					.WithColumn("OwnerUserId").AsString(450).Nullable()
					.WithColumn("DmKey").AsString(450).Nullable()
					.WithColumn("IsArchived").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ArchivedOn").AsDateTime2().Nullable()
					.WithColumn("IsLocked").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LockedByUserId").AsString(450).Nullable()
					.WithColumn("LockedOn").AsDateTime2().Nullable()
					.WithColumn("LastMessageSeq").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("LastMessageOn").AsDateTime2().Nullable()
					.WithColumn("RetentionOverrideDays").AsInt32().Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.Index("IX_ChatChannels_Department_Type")
					.OnTable("ChatChannels")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("ChannelType").Ascending();

				Create.Index("IX_ChatChannels_CallId")
					.OnTable("ChatChannels")
					.OnColumn("CallId").Ascending();

				// DM dedup: at most one channel per normalized participant key per department.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannels_Department_DmKey ON ChatChannels (DepartmentId, DmKey) WHERE DmKey IS NOT NULL;");

				// At most one lane channel per command structure node.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannels_Node ON ChatChannels (CommandStructureNodeId) WHERE CommandStructureNodeId IS NOT NULL;");

				// At most one chatbot conversation per user per department (ChannelType 8 = Chatbot).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannels_Department_Owner_Bot ON ChatChannels (DepartmentId, OwnerUserId) WHERE ChannelType = 8 AND OwnerUserId IS NOT NULL;");

				// At most one default channel per group (ChannelType 3 = GroupDefault).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannels_Group_Default ON ChatChannels (GroupId) WHERE ChannelType = 3 AND GroupId IS NOT NULL;");

				// At most one default channel per department (ChannelType 2 = DepartmentDefault).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannels_Department_Default ON ChatChannels (DepartmentId) WHERE ChannelType = 2;");
			}

			if (!Schema.Table("ChatChannelAccessRules").Exists())
			{
				Create.Table("ChatChannelAccessRules")
					.WithColumn("ChatChannelAccessRuleId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RuleType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("GroupId").AsInt32().Nullable()
					.WithColumn("PersonnelRoleId").AsInt32().Nullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("AddedByUserId").AsString(450).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatChannelAccessRules_Channel")
					.OnTable("ChatChannelAccessRules")
					.OnColumn("ChatChannelId").Ascending();
			}

			if (!Schema.Table("ChatChannelMembers").Exists())
			{
				Create.Table("ChatChannelMembers")
					.WithColumn("ChatChannelMemberId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ParticipantType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("DisplayNameOverride").AsString(int.MaxValue).Nullable()
					.WithColumn("IsModerator").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("JoinedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId").AsString(450).Nullable()
					.WithColumn("RemovedOn").AsDateTime2().Nullable()
					.WithColumn("LastReadSeq").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("LastReadOn").AsDateTime2().Nullable()
					.WithColumn("LastDeliveredSeq").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("MutedUntil").AsDateTime2().Nullable()
					.WithColumn("IsBanned").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("BannedOn").AsDateTime2().Nullable()
					.WithColumn("BannedByUserId").AsString(450).Nullable()
					.WithColumn("NotificationPreference").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.Index("IX_ChatChannelMembers_Channel")
					.OnTable("ChatChannelMembers")
					.OnColumn("ChatChannelId").Ascending();

				Create.Index("IX_ChatChannelMembers_Department_User")
					.OnTable("ChatChannelMembers")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UserId").Ascending();

				// At most one membership row per person per channel (ParticipantType 0 = User).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannelMembers_Channel_User ON ChatChannelMembers (ChatChannelId, UserId) WHERE ParticipantType = 0;");

				// At most one membership row per unit per channel (ParticipantType 1 = Unit).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatChannelMembers_Channel_Unit ON ChatChannelMembers (ChatChannelId, UnitId) WHERE ParticipantType = 1;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatChannelMembers").Exists())
			{
				// Explicit index drops (the table drop would also remove them, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannelMembers_Channel_User ON ChatChannelMembers;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannelMembers_Channel_Unit ON ChatChannelMembers;");

				Delete.Table("ChatChannelMembers");
			}

			if (Schema.Table("ChatChannelAccessRules").Exists())
				Delete.Table("ChatChannelAccessRules");

			if (Schema.Table("ChatChannels").Exists())
			{
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannels_Department_DmKey ON ChatChannels;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannels_Node ON ChatChannels;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannels_Department_Owner_Bot ON ChatChannels;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannels_Group_Default ON ChatChannels;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatChannels_Department_Default ON ChatChannels;");

				Delete.Table("ChatChannels");
			}
		}
	}
}
