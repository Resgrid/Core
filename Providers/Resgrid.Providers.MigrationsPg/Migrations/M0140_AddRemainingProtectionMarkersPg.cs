using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Row markers for the last of the plan's protected-field candidates (sections 5.2 and 22.1):
	/// unit logs, user state notes, calendar items, documents, and the dormant mailbox credentials
	/// on distribution lists. isprotected says "this row's cataloged values carry rgdp envelopes".
	///
	/// Only the marker is needed. every text column here is citext (unbounded) and
	/// documents.data is bytea, which holds an rgdpb envelope as-is.
	///
	/// Additive and inert until the department is enrolled and the catalog-upgrade sweep runs.
	/// </summary>
	[Migration(140)]
	public class M0140_AddRemainingProtectionMarkersPg : Migration
	{
		private static readonly string[] Tables =
		{
			"unitlogs",
			"userstates",
			"calendaritems",
			"documents",
			"distributionlists"
		};

		public override void Up()
		{
			foreach (var table in Tables)
			{
				if (!Schema.Table(table).Column("isprotected").Exists())
					Alter.Table(table)
						.AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			// Refuses rather than relying on the operator having read the comment below. Dropping the
			// marker off a table that still holds enveloped rows leaves ciphertext with nothing to
			// identify it by, and a later re-apply recreates the marker as false - so those rows read
			// as unprotected while their values are unreadable.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("isprotected").Exists())
					Execute.Sql($@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM {table} WHERE isprotected = true) THEN
		RAISE EXCEPTION 'M0140 rollback refused: protected rows exist in {table} and dropping isprotected would orphan their envelopes. Offboard the affected departments first.';
	END IF;
END $$;");
			}

			// The marker only. The values themselves stay exactly as they are: for an enrolled
			// department these columns hold the ONLY copy, and dropping or narrowing them would
			// destroy data.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("isprotected").Exists())
					Delete.Column("isprotected").FromTable(table);
			}
		}
	}
}
