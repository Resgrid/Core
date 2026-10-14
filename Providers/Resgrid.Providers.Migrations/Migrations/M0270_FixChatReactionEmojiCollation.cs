using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// ChatMessageReactions.Emoji moves to a binary collation. Under the database default (SQL_Latin1_General_CP1_CI_AS)
	/// every emoji outside the Basic Multilingual Plane has no sort weight, so N'🙏' = N'🔥' = N'👍' = N'' are all true:
	/// UX_ChatMessageReactions_User / _Unit allowed one such emoji per participant per message, a second one failed the
	/// insert with error 2601 (swallowed by ChatMessageService, so the reaction silently vanished), and RemoveReaction's
	/// [Emoji] = @Emoji matched whichever emoji the participant had. Latin1_General_100_BIN2 compares code points, the same
	/// StringComparison.Ordinal the service's duplicate pre-check uses. A finer comparison cannot create duplicates, so no
	/// row changes; the two filtered unique indexes are dropped around the ALTER and recreated as M0106 defined them.
	/// </summary>
	[Migration(270)]
	public class M0270_FixChatReactionEmojiCollation : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ChatMessageReactions").Column("Emoji").Exists())
				return;

			DropReactionIndexes();
			Execute.Sql("ALTER TABLE ChatMessageReactions ALTER COLUMN Emoji nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL;");
			CreateReactionIndexes();
		}

		public override void Down()
		{
			if (!Schema.Table("ChatMessageReactions").Column("Emoji").Exists())
				return;

			DropReactionIndexes();
			Execute.Sql("ALTER TABLE ChatMessageReactions ALTER COLUMN Emoji nvarchar(64) COLLATE DATABASE_DEFAULT NULL;");

			// Under the default collation distinct emoji collide again; keep each participant's earliest reaction so the
			// unique indexes can be rebuilt.
			Execute.Sql(@"WITH ranked AS (
					SELECT ROW_NUMBER() OVER (PARTITION BY ChatMessageId, ParticipantType, UserId, UnitId, Emoji
						ORDER BY ReactedOn, ChatMessageReactionId) AS place
					FROM ChatMessageReactions WHERE ParticipantType IN (0, 1))
				DELETE FROM ranked WHERE place > 1;");

			CreateReactionIndexes();
		}

		private void DropReactionIndexes()
		{
			Execute.Sql("DROP INDEX IF EXISTS UX_ChatMessageReactions_User ON ChatMessageReactions;");
			Execute.Sql("DROP INDEX IF EXISTS UX_ChatMessageReactions_Unit ON ChatMessageReactions;");
		}

		private void CreateReactionIndexes()
		{
			// One reaction per emoji per person (ParticipantType 0 = User).
			Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatMessageReactions_User ON ChatMessageReactions (ChatMessageId, UserId, Emoji) WHERE ParticipantType = 0;");

			// One reaction per emoji per unit (ParticipantType 1 = Unit).
			Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_ChatMessageReactions_Unit ON ChatMessageReactions (ChatMessageId, UnitId, Emoji) WHERE ParticipantType = 1;");
		}
	}
}
