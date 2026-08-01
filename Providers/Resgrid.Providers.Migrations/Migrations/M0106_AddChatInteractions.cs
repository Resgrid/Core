using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Chat interaction tables: ChatMessageReactions (one emoji reaction per message/participant/emoji,
	/// enforced per participant kind via filtered unique indexes), ChatMessageMentions (@mention rows
	/// driving notifications and "mentions of me" queries) and ChatMessageAcks (required acknowledgments
	/// provisioned per user for urgent messages; unit audiences expand to the roster).
	/// </summary>
	[Migration(106)]
	public class M0106_AddChatInteractions : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessageReactions").Exists())
			{
				Create.Table("ChatMessageReactions")
					.WithColumn("ChatMessageReactionId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ParticipantType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					// AsString(64) rather than AsString(int.MaxValue): Emoji participates in the unique
					// indexes below and nvarchar(max) columns cannot be index key columns.
					.WithColumn("Emoji").AsString(64).Nullable()
					.WithColumn("ReactedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatMessageReactions_Channel")
					.OnTable("ChatMessageReactions")
					.OnColumn("ChatChannelId").Ascending();

				// One reaction per emoji per person (ParticipantType 0 = User).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatMessageReactions_User ON ChatMessageReactions (ChatMessageId, UserId, Emoji) WHERE ParticipantType = 0;");

				// One reaction per emoji per unit (ParticipantType 1 = Unit).
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatMessageReactions_Unit ON ChatMessageReactions (ChatMessageId, UnitId, Emoji) WHERE ParticipantType = 1;");
			}

			if (!Schema.Table("ChatMessageMentions").Exists())
			{
				Create.Table("ChatMessageMentions")
					.WithColumn("ChatMessageMentionId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("MentionType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TargetUserId").AsString(450).Nullable()
					.WithColumn("TargetUnitId").AsInt32().Nullable()
					.WithColumn("TargetRoleId").AsInt32().Nullable()
					.WithColumn("TargetGroupId").AsInt32().Nullable();

				Create.Index("IX_ChatMessageMentions_Message")
					.OnTable("ChatMessageMentions")
					.OnColumn("ChatMessageId").Ascending();

				Create.Index("IX_ChatMessageMentions_Department_TargetUser")
					.OnTable("ChatMessageMentions")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("TargetUserId").Ascending();
			}

			if (!Schema.Table("ChatMessageAcks").Exists())
			{
				Create.Table("ChatMessageAcks")
					.WithColumn("ChatMessageAckId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(450).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("RequiredOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable();

				Create.Index("IX_ChatMessageAcks_Message")
					.OnTable("ChatMessageAcks")
					.OnColumn("ChatMessageId").Ascending();

				Create.Index("IX_ChatMessageAcks_Department_User_AcknowledgedOn")
					.OnTable("ChatMessageAcks")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UserId").Ascending()
					.OnColumn("AcknowledgedOn").Ascending();

				// One ack requirement row per user per message.
				Create.Index("UX_ChatMessageAcks_Message_User")
					.OnTable("ChatMessageAcks")
					.OnColumn("ChatMessageId").Ascending()
					.OnColumn("UserId").Ascending()
					.WithOptions().Unique();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatMessageAcks").Exists())
				Delete.Table("ChatMessageAcks");

			if (Schema.Table("ChatMessageMentions").Exists())
				Delete.Table("ChatMessageMentions");

			if (Schema.Table("ChatMessageReactions").Exists())
			{
				// Explicit index drops (the table drop would also remove them, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatMessageReactions_User ON ChatMessageReactions;");
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatMessageReactions_Unit ON ChatMessageReactions;");

				Delete.Table("ChatMessageReactions");
			}
		}
	}
}
