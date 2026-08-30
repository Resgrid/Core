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
			// The marker only: for an enrolled department the columns beside it hold the only copy
			// of the values, and dropping or narrowing them would destroy data.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("IsProtected").Exists())
					Delete.Column("IsProtected").FromTable(table);
			}
		}
	}
}
