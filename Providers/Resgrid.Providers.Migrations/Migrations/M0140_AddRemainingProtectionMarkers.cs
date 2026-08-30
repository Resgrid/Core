using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Row markers for the last of the plan's protected-field candidates (sections 5.2 and 22.1):
	/// unit logs, user state notes, calendar items, documents, and the dormant mailbox credentials
	/// on distribution lists. IsProtected says "this row's cataloged values carry rgdp envelopes".
	///
	/// Only the marker is needed. M0127 widened UnitLogs.Narrative, UserStates.Note and the
	/// DistributionLists credential columns; Documents and CalendarItems were already NVARCHAR(MAX)
	/// where it matters, and Documents.Data is VARBINARY(MAX), which holds an rgdpb envelope as-is.
	///
	/// Additive and inert until the department is enrolled and the catalog-upgrade sweep runs.
	/// </summary>
	[Migration(140)]
	public class M0140_AddRemainingProtectionMarkers : Migration
	{
		private static readonly string[] Tables =
		{
			"UnitLogs",
			"UserStates",
			"CalendarItems",
			"Documents",
			"DistributionLists"
		};

		public override void Up()
		{
			foreach (var table in Tables)
			{
				if (!Schema.Table(table).Column("IsProtected").Exists())
					Alter.Table(table)
						.AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);
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
				if (Schema.Table(table).Column("IsProtected").Exists())
					Execute.Sql($@"
IF EXISTS (SELECT 1 FROM [{table}] WHERE [IsProtected] = 1)
	THROW 51000, 'M0140 rollback refused: protected rows exist in {table} and dropping IsProtected would orphan their envelopes. Offboard the affected departments first.', 1;");
			}

			// The marker only. The values themselves stay exactly as they are: for an enrolled
			// department these columns hold the ONLY copy, and dropping or narrowing them would
			// destroy data.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("IsProtected").Exists())
					Delete.Column("IsProtected").FromTable(table);
			}
		}
	}
}
