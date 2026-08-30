using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Separates machine metadata from the member's own words on MessageRecipients.
	///
	/// Note was doing two jobs: it held whatever a member typed when answering a message, AND the
	/// TextResponsePromptMetadata token that says which calendar item or poll a prompt belongs to
	/// ("calendar-rsvp:42", "poll:7"). That token is parsed by the chatbot inbound resolver, the
	/// RSVP prompt service and both message controllers — paths that hold no Protected Data Grant,
	/// against a broker whose workload lane is encrypt-only. So while the two shared a column, Note
	/// could not enter the protected-field catalog: encrypting it would have silently broken
	/// calendar RSVP and poll replies for the departments that turned protection on.
	///
	/// With the token in its own column, Note becomes ordinary member free text and joins the
	/// protected-field catalog with the rest of the message family (v7). PromptMetadata stays
	/// plaintext deliberately — it is a row pointer, holds nothing about a person, and every reader
	/// of it runs without a grant.
	///
	/// The backfill MOVES the token rather than copying it: leaving a duplicate behind would put a
	/// machine token inside the column that is about to be encrypted, for no reader.
	/// </summary>
	[Migration(138)]
	public class M0138_AddMessageRecipientPromptMetadata : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("MessageRecipients").Column("PromptMetadata").Exists())
				Alter.Table("MessageRecipients").AddColumn("PromptMetadata").AsString(int.MaxValue).Nullable();

			// Only rows whose Note IS a token — the prefixes are the ones TextResponsePromptMetadata
			// writes. A member's typed note is left exactly where it is.
			Execute.Sql(@"
UPDATE [MessageRecipients]
SET [PromptMetadata] = [Note], [Note] = NULL
WHERE [PromptMetadata] IS NULL
  AND [Note] IS NOT NULL
  AND ([Note] LIKE 'calendar-rsvp:%' OR [Note] LIKE 'poll:%');");
		}

		public override void Down()
		{
			// Put the tokens back before the column disappears, or the prompts they point at become
			// unanswerable. Only where Note is free — a member's note must never be overwritten.
			Execute.Sql(@"
UPDATE [MessageRecipients]
SET [Note] = [PromptMetadata], [PromptMetadata] = NULL
WHERE [PromptMetadata] IS NOT NULL AND ([Note] IS NULL OR LTRIM(RTRIM([Note])) = '');");

			if (Schema.Table("MessageRecipients").Column("PromptMetadata").Exists())
				Delete.Column("PromptMetadata").FromTable("MessageRecipients");
		}
	}
}
