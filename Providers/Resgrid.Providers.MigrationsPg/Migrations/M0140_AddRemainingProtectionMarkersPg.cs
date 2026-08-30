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
			// The marker only: for an enrolled department the columns beside it hold the only copy
			// of the values, and dropping or narrowing them would destroy data.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("isprotected").Exists())
					Delete.Column("isprotected").FromTable(table);
			}
		}
	}
}
