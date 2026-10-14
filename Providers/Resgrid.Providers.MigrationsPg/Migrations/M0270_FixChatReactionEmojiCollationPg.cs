using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Version-parity no-op for the SQL Server M0270, which gives ChatMessageReactions.Emoji a binary collation because the
	/// SQL Server default collation compares every non-BMP emoji as equal. chatmessagereactions.emoji is citext here: citext
	/// equality compares the lowercased bytes, and its btree ordering breaks collation ties byte-wise, so distinct emoji are
	/// already distinct keys in ux_chatmessagereactions_user / _unit and nothing changes.
	/// </summary>
	[Migration(270)]
	public class M0270_FixChatReactionEmojiCollationPg : Migration
	{
		public override void Up()
		{
			// Intentionally empty; see summary.
		}

		public override void Down()
		{
			// Intentionally empty; see summary.
		}
	}
}
