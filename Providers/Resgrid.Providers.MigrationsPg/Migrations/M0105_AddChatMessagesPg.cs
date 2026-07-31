using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Chat message tables: ChatMessages (immutable-for-audit bodies with per-channel monotonic
	/// MessageSeq, threading, priority and tombstone deletes), ChatMessageEdits (prior-body audit
	/// history for edits/deletes) and ChatAttachments (BLOB-in-DB files/images with channel/department
	/// scoping for auth and retention purge). A partial unique index on the client-supplied
	/// ClientMessageId makes offline outbox retries idempotent.
	/// </summary>
	[Migration(105)]
	public class M0105_AddChatMessagesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessages".ToLower()).Exists())
			{
				Create.Table("ChatMessages".ToLower())
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("MessageSeq".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderParticipantType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SenderUnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("SenderDisplayName".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Body".ToLower()).AsCustom("text").Nullable()
					.WithColumn("MessageType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThreadRootMessageId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ThreadReplyCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastThreadReplyOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("AlsoSendToChannel".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("MetadataJson".ToLower()).AsCustom("text").Nullable()
					.WithColumn("ClientMessageId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SentOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("EditedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("DeletedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("DeletedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("PinnedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("PinnedByUserId".ToLower()).AsCustom("citext").Nullable();

				// One MessageSeq value per channel; backstops the atomic allocation from ChatChannels.LastMessageSeq.
				Create.Index("UX_ChatMessages_Channel_Seq".ToLower())
					.OnTable("ChatMessages".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending()
					.OnColumn("MessageSeq".ToLower()).Ascending()
					.WithOptions().Unique();

				Create.Index("IX_ChatMessages_Channel_Thread_Seq".ToLower())
					.OnTable("ChatMessages".ToLower())
					.OnColumn("ChatChannelId".ToLower()).Ascending()
					.OnColumn("ThreadRootMessageId".ToLower()).Ascending()
					.OnColumn("MessageSeq".ToLower()).Ascending();

				Create.Index("IX_ChatMessages_Department_SentOn".ToLower())
					.OnTable("ChatMessages".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("SentOn".ToLower()).Ascending();

				// Idempotency for the mobile offline outbox: a retried send with the same client key dedups.
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_chatmessages_client ON chatmessages (chatchannelid, senderuserid, clientmessageid) WHERE clientmessageid IS NOT NULL;");
			}

			if (!Schema.Table("ChatMessageEdits".ToLower()).Exists())
			{
				Create.Table("ChatMessageEdits".ToLower())
					.WithColumn("ChatMessageEditId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("PriorBody".ToLower()).AsCustom("text").Nullable()
					.WithColumn("EditType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EditedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("EditedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatMessageEdits_Message".ToLower())
					.OnTable("ChatMessageEdits".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending();
			}

			if (!Schema.Table("ChatAttachments".ToLower()).Exists())
			{
				Create.Table("ChatAttachments".ToLower())
					.WithColumn("ChatAttachmentId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("ChatChannelId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("FileName".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ContentType".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Size".ToLower()).AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Sha256".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Data".ToLower()).AsCustom("bytea").Nullable()
					.WithColumn("ThumbnailData".ToLower()).AsCustom("bytea").Nullable()
					.WithColumn("UploadedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UploadedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatAttachments_Message".ToLower())
					.OnTable("ChatAttachments".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending();

				Create.Index("IX_ChatAttachments_Department_UploadedOn".ToLower())
					.OnTable("ChatAttachments".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("UploadedOn".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatAttachments".ToLower()).Exists())
				Delete.Table("ChatAttachments".ToLower());

			if (Schema.Table("ChatMessageEdits".ToLower()).Exists())
				Delete.Table("ChatMessageEdits".ToLower());

			if (Schema.Table("ChatMessages".ToLower()).Exists())
			{
				// Explicit index drop (the table drop would also remove it, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS ux_chatmessages_client;");

				Delete.Table("ChatMessages".ToLower());
			}
		}
	}
}
