using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Chat hot-path indexes: thread pages (ThreadRootMessageId + MessageSeq), channel-member lookups by
	/// (channel, user) and (channel, unit) used on every post/permission evaluation, and reactions by
	/// message for rendering. Also adds the per-department ChatbotFallbackEnabled toggle to
	/// ChatDepartmentSettings (mirrors ChatConfig.ChatbotFallbackEnabled).
	/// </summary>
	[Migration(109)]
	public class M0109_AddChatHotPathIndexesPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("ChatMessages".ToLower()).Exists() && !Schema.Table("ChatMessages".ToLower()).Index("IX_ChatMessages_ThreadRoot".ToLower()).Exists())
			{
				Create.Index("IX_ChatMessages_ThreadRoot".ToLower())
					.OnTable("ChatMessages".ToLower())
					.OnColumn("ThreadRootMessageId".ToLower()).Ascending()
					.OnColumn("MessageSeq".ToLower()).Ascending();
			}

			if (Schema.Table("ChatChannelMembers".ToLower()).Exists())
			{
				if (!Schema.Table("ChatChannelMembers".ToLower()).Index("IX_ChatChannelMembers_ChannelUser".ToLower()).Exists())
				{
					Create.Index("IX_ChatChannelMembers_ChannelUser".ToLower())
						.OnTable("ChatChannelMembers".ToLower())
						.OnColumn("ChatChannelId".ToLower()).Ascending()
						.OnColumn("UserId".ToLower()).Ascending();
				}

				if (!Schema.Table("ChatChannelMembers".ToLower()).Index("IX_ChatChannelMembers_ChannelUnit".ToLower()).Exists())
				{
					Create.Index("IX_ChatChannelMembers_ChannelUnit".ToLower())
						.OnTable("ChatChannelMembers".ToLower())
						.OnColumn("ChatChannelId".ToLower()).Ascending()
						.OnColumn("UnitId".ToLower()).Ascending();
				}
			}

			if (Schema.Table("ChatMessageReactions".ToLower()).Exists() && !Schema.Table("ChatMessageReactions".ToLower()).Index("IX_ChatMessageReactions_Message".ToLower()).Exists())
			{
				Create.Index("IX_ChatMessageReactions_Message".ToLower())
					.OnTable("ChatMessageReactions".ToLower())
					.OnColumn("ChatMessageId".ToLower()).Ascending();
			}

			if (Schema.Table("ChatDepartmentSettings".ToLower()).Exists() && !Schema.Table("ChatDepartmentSettings".ToLower()).Column("ChatbotFallbackEnabled".ToLower()).Exists())
			{
				Alter.Table("ChatDepartmentSettings".ToLower())
					.AddColumn("ChatbotFallbackEnabled".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("ChatDepartmentSettings".ToLower()).Exists() && Schema.Table("ChatDepartmentSettings".ToLower()).Column("ChatbotFallbackEnabled".ToLower()).Exists())
				Delete.Column("ChatbotFallbackEnabled".ToLower()).FromTable("ChatDepartmentSettings".ToLower());

			if (Schema.Table("ChatMessageReactions".ToLower()).Exists() && Schema.Table("ChatMessageReactions".ToLower()).Index("IX_ChatMessageReactions_Message".ToLower()).Exists())
				Delete.Index("IX_ChatMessageReactions_Message".ToLower()).OnTable("ChatMessageReactions".ToLower());

			if (Schema.Table("ChatChannelMembers".ToLower()).Exists())
			{
				if (Schema.Table("ChatChannelMembers".ToLower()).Index("IX_ChatChannelMembers_ChannelUnit".ToLower()).Exists())
					Delete.Index("IX_ChatChannelMembers_ChannelUnit".ToLower()).OnTable("ChatChannelMembers".ToLower());

				if (Schema.Table("ChatChannelMembers".ToLower()).Index("IX_ChatChannelMembers_ChannelUser".ToLower()).Exists())
					Delete.Index("IX_ChatChannelMembers_ChannelUser".ToLower()).OnTable("ChatChannelMembers".ToLower());
			}

			if (Schema.Table("ChatMessages".ToLower()).Exists() && Schema.Table("ChatMessages".ToLower()).Index("IX_ChatMessages_ThreadRoot".ToLower()).Exists())
				Delete.Index("IX_ChatMessages_ThreadRoot".ToLower()).OnTable("ChatMessages".ToLower());
		}
	}
}
