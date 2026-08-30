using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Separates machine metadata from the member's own words on messagerecipients.
	///
	/// note was doing two jobs: it held whatever a member typed when answering a message, AND the
	/// TextResponsePromptMetadata token that says which calendar item or poll a prompt belongs to
	/// ("calendar-rsvp:42", "poll:7"). That token is parsed by the chatbot inbound resolver, the
	/// RSVP prompt service and both message controllers — paths that hold no Protected Data Grant,
	/// against a broker whose workload lane is encrypt-only. So while the two shared a column, note
	/// could not enter the protected-field catalog: encrypting it would have silently broken
	/// calendar RSVP and poll replies for the departments that turned protection on.
	///
	/// With the token in its own column, note becomes ordinary member free text and joins the
	/// protected-field catalog with the rest of the message family (v7). promptmetadata stays
	/// plaintext deliberately — it is a row pointer, holds nothing about a person, and every reader
	/// of it runs without a grant.
	///
	/// The backfill MOVES the token rather than copying it: leaving a duplicate behind would put a
	/// machine token inside the column that is about to be encrypted, for no reader.
	/// </summary>
	[Migration(138)]
	public class M0138_AddMessageRecipientPromptMetadataPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("messagerecipients").Column("promptmetadata").Exists())
				Alter.Table("messagerecipients").AddColumn("promptmetadata").AsCustom("citext").Nullable();

			// Only rows whose note IS a token — the prefixes are the ones TextResponsePromptMetadata
			// writes. A member's typed note is left exactly where it is.
			Execute.Sql(@"
UPDATE messagerecipients
SET promptmetadata = note, note = NULL
WHERE promptmetadata IS NULL
  AND note IS NOT NULL
  AND (note LIKE 'calendar-rsvp:%' OR note LIKE 'poll:%');");
		}

		public override void Down()
		{
			// Put the tokens back before the column disappears, or the prompts they point at become
			// unanswerable. Only where note is free — a member's note must never be overwritten.
			Execute.Sql(@"
UPDATE messagerecipients
SET note = promptmetadata, promptmetadata = NULL
WHERE promptmetadata IS NOT NULL AND (note IS NULL OR btrim(note::text) = '');");

			if (Schema.Table("messagerecipients").Column("promptmetadata").Exists())
				Delete.Column("promptmetadata").FromTable("messagerecipients");
		}
	}
}
