using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Chat interaction tables: ChatMessageReactions (one emoji reaction per message/participant/emoji,
	/// enforced per participant kind via partial unique indexes), ChatMessageMentions (@mention rows
	/// driving notifications and "mentions of me" queries) and ChatMessageAcks (required acknowledgments
	/// provisioned per user for urgent messages; unit audiences expand to the roster).
	/// </summary>
	[Migration(106)]
	public class M0106_AddChatInteractionsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessageReactions".ToLower()).Exists())
			{
				Create.Table("ChatMessageReactions".ToLower())
					.WithColumn("ChatMessageReactionId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("ParticipantType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("Emoji".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ReactedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatMessageReactions_Channel".ToLower())
					.OnTable("ChatMessageReactions".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending();

				// One reaction per emoji per person (ParticipantType 0 = User).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatmessagereactions_user ON chatmessagereactions (chatmessageid, userid, emoji) WHERE participanttype = 0;");

				// One reaction per emoji per unit (ParticipantType 1 = Unit).
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatmessagereactions_unit ON chatmessagereactions (chatmessageid, unitid, emoji) WHERE participanttype = 1;");
			}

			if (!Schema.Table("ChatMessageMentions".ToLower()).Exists())
			{
				Create.Table("ChatMessageMentions".ToLower())
					.WithColumn("ChatMessageMentionId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("MentionType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TargetUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("TargetUnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("TargetRoleId".ToLower()).AsInt32().Nullable()
					.WithColumn("TargetGroupId".ToLower()).AsInt32().Nullable();

				Create.Index("IX_ChatMessageMentions_Message".ToLower())
					.OnTable("ChatMessageMentions".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending();

				Create.Index("IX_ChatMessageMentions_Department_TargetUser".ToLower())
					.OnTable("ChatMessageMentions".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("TargetUserId".ToLower()).Ascending();
			}

			if (!Schema.Table("ChatMessageAcks".ToLower()).Exists())
			{
				Create.Table("ChatMessageAcks".ToLower())
					.WithColumn("ChatMessageAckId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("UserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("RequiredOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AcknowledgedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_ChatMessageAcks_Message".ToLower())
					.OnTable("ChatMessageAcks".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending();

				Create.Index("IX_ChatMessageAcks_Department_User_AcknowledgedOn".ToLower())
					.OnTable("ChatMessageAcks".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("UserId".ToLower()).Ascending()
					.OnColumn("AcknowledgedOn".ToLower()).Ascending();

				// One ack requirement row per user per message.
				Create.Index("UX_ChatMessageAcks_Message_User".ToLower())
					.OnTable("ChatMessageAcks".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending()
					.OnColumn("UserId".ToLower()).Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatMessageAcks".ToLower()).Exists())
				Delete.Table("ChatMessageAcks".ToLower());

			if (Schema.Table("ChatMessageMentions".ToLower()).Exists())
				Delete.Table("ChatMessageMentions".ToLower());

			if (Schema.Table("ChatMessageReactions".ToLower()).Exists())
			{
				// Explicit index drops (the table drop would also remove them, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS ux_chatmessagereactions_user;");
				Execute.Sql("DROP INDEX IF EXISTS ux_chatmessagereactions_unit;");

				Delete.Table("ChatMessageReactions".ToLower());
			}
		}
	}
}
