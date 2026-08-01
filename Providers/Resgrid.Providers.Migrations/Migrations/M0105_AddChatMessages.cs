using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Chat message tables: ChatMessages (immutable-for-audit bodies with per-channel monotonic
	/// MessageSeq, threading, priority and tombstone deletes), ChatMessageEdits (prior-body audit
	/// history for edits/deletes) and ChatAttachments (BLOB-in-DB files/images with channel/department
	/// scoping for auth and retention purge). A filtered unique index on the client-supplied
	/// ClientMessageId makes offline outbox retries idempotent.
	/// </summary>
	[Migration(105)]
	public class M0105_AddChatMessages : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessages").Exists())
			{
				Create.Table("ChatMessages")
					.WithColumn("ChatMessageId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("MessageSeq").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderParticipantType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SenderUserId").AsString(450).Nullable()
					.WithColumn("SenderUnitId").AsInt32().Nullable()
					.WithColumn("SenderDisplayName").AsString(int.MaxValue).Nullable()
					.WithColumn("Body").AsString(int.MaxValue).Nullable()
					.WithColumn("MessageType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ThreadRootMessageId").AsString(128).Nullable()
					.WithColumn("ThreadReplyCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastThreadReplyOn").AsDateTime2().Nullable()
					.WithColumn("AlsoSendToChannel").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("MetadataJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ClientMessageId").AsString(128).Nullable()
					.WithColumn("SentOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("EditedOn").AsDateTime2().Nullable()
					.WithColumn("DeletedOn").AsDateTime2().Nullable()
					.WithColumn("DeletedByUserId").AsString(450).Nullable()
					.WithColumn("PinnedOn").AsDateTime2().Nullable()
					.WithColumn("PinnedByUserId").AsString(450).Nullable();

				// One MessageSeq value per channel; backstops the atomic allocation from ChatChannels.LastMessageSeq.
				Create.Index("UX_ChatMessages_Channel_Seq")
					.OnTable("ChatMessages")
					.OnColumn("ChatChannelId").Ascending()
					.OnColumn("MessageSeq").Ascending()
					.WithOptions().Unique();

				Create.Index("IX_ChatMessages_Channel_Thread_Seq")
					.OnTable("ChatMessages")
					.OnColumn("ChatChannelId").Ascending()
					.OnColumn("ThreadRootMessageId").Ascending()
					.OnColumn("MessageSeq").Ascending();

				Create.Index("IX_ChatMessages_Department_SentOn")
					.OnTable("ChatMessages")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("SentOn").Ascending();

				// Idempotency for the mobile offline outbox: a retried send with the same client key dedups.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatMessages_Client ON ChatMessages (ChatChannelId, SenderUserId, ClientMessageId) WHERE ClientMessageId IS NOT NULL;");
			}

			if (!Schema.Table("ChatMessageEdits").Exists())
			{
				Create.Table("ChatMessageEdits")
					.WithColumn("ChatMessageEditId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("PriorBody").AsString(int.MaxValue).Nullable()
					.WithColumn("EditType").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EditedByUserId").AsString(450).Nullable()
					.WithColumn("EditedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatMessageEdits_Message")
					.OnTable("ChatMessageEdits")
					.OnColumn("ChatMessageId").Ascending();
			}

			if (!Schema.Table("ChatAttachments").Exists())
			{
				Create.Table("ChatAttachments")
					.WithColumn("ChatAttachmentId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ChatMessageId").AsString(128).NotNullable()
					.WithColumn("ChatChannelId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("FileName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContentType").AsString(int.MaxValue).Nullable()
					.WithColumn("Size").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Sha256").AsString(int.MaxValue).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("ThumbnailData").AsBinary(int.MaxValue).Nullable()
					.WithColumn("UploadedByUserId").AsString(450).Nullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("IX_ChatAttachments_Message")
					.OnTable("ChatAttachments")
					.OnColumn("ChatMessageId").Ascending();

				Create.Index("IX_ChatAttachments_Department_UploadedOn")
					.OnTable("ChatAttachments")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UploadedOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatAttachments").Exists())
				Delete.Table("ChatAttachments");

			if (Schema.Table("ChatMessageEdits").Exists())
				Delete.Table("ChatMessageEdits");

			if (Schema.Table("ChatMessages").Exists())
			{
				// Explicit index drop (the table drop would also remove it, but be explicit to mirror the codebase pattern).
				Execute.Sql("DROP INDEX IF EXISTS UX_ChatMessages_Client ON ChatMessages;");

				Delete.Table("ChatMessages");
			}
		}
	}
}
