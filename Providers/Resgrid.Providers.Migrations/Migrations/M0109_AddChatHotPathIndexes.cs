using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Chat hot-path indexes: thread pages (ThreadRootMessageId + MessageSeq), channel-member lookups by
	/// (channel, user) and (channel, unit) used on every post/permission evaluation, and reactions by
	/// message for rendering. Also adds the per-department ChatbotFallbackEnabled toggle to
	/// ChatDepartmentSettings (mirrors ChatConfig.ChatbotFallbackEnabled).
	/// </summary>
	[Migration(109)]
	public class M0109_AddChatHotPathIndexes : Migration
	{
		public override void Up()
		{
			if (Schema.Table("ChatMessages").Exists() && !Schema.Table("ChatMessages").Index("IX_ChatMessages_ThreadRoot").Exists())
			{
				Create.Index("IX_ChatMessages_ThreadRoot")
					.OnTable("ChatMessages")
					.OnColumn("ThreadRootMessageId").Ascending()
					.OnColumn("MessageSeq").Ascending();
			}

			if (Schema.Table("ChatChannelMembers").Exists())
			{
				if (!Schema.Table("ChatChannelMembers").Index("IX_ChatChannelMembers_ChannelUser").Exists())
				{
					Create.Index("IX_ChatChannelMembers_ChannelUser")
						.OnTable("ChatChannelMembers")
						.OnColumn("ChatChannelId").Ascending()
						.OnColumn("UserId").Ascending();
				}

				if (!Schema.Table("ChatChannelMembers").Index("IX_ChatChannelMembers_ChannelUnit").Exists())
				{
					Create.Index("IX_ChatChannelMembers_ChannelUnit")
						.OnTable("ChatChannelMembers")
						.OnColumn("ChatChannelId").Ascending()
						.OnColumn("UnitId").Ascending();
				}
			}

			if (Schema.Table("ChatMessageReactions").Exists() && !Schema.Table("ChatMessageReactions").Index("IX_ChatMessageReactions_Message").Exists())
			{
				Create.Index("IX_ChatMessageReactions_Message")
					.OnTable("ChatMessageReactions")
					.OnColumn("ChatMessageId").Ascending();
			}

			if (Schema.Table("ChatDepartmentSettings").Exists() && !Schema.Table("ChatDepartmentSettings").Column("ChatbotFallbackEnabled").Exists())
			{
				Alter.Table("ChatDepartmentSettings")
					.AddColumn("ChatbotFallbackEnabled").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatDepartmentSettings").Exists() && Schema.Table("ChatDepartmentSettings").Column("ChatbotFallbackEnabled").Exists())
				Delete.Column("ChatbotFallbackEnabled").FromTable("ChatDepartmentSettings");

			if (Schema.Table("ChatMessageReactions").Exists() && Schema.Table("ChatMessageReactions").Index("IX_ChatMessageReactions_Message").Exists())
				Delete.Index("IX_ChatMessageReactions_Message").OnTable("ChatMessageReactions");

			if (Schema.Table("ChatChannelMembers").Exists())
			{
				if (Schema.Table("ChatChannelMembers").Index("IX_ChatChannelMembers_ChannelUnit").Exists())
					Delete.Index("IX_ChatChannelMembers_ChannelUnit").OnTable("ChatChannelMembers");

				if (Schema.Table("ChatChannelMembers").Index("IX_ChatChannelMembers_ChannelUser").Exists())
					Delete.Index("IX_ChatChannelMembers_ChannelUser").OnTable("ChatChannelMembers");
			}

			if (Schema.Table("ChatMessages").Exists() && Schema.Table("ChatMessages").Index("IX_ChatMessages_ThreadRoot").Exists())
				Delete.Index("IX_ChatMessages_ThreadRoot").OnTable("ChatMessages");
		}
	}
}
