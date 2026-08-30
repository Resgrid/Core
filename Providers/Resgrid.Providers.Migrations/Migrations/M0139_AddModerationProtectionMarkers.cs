using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Row markers for the moderation family entering the protected-field catalog (ADP plan
	/// section 5.3). IsProtected says "this row's cataloged values carry rgdp envelopes" — the same
	/// marker Calls' children, unit states, member data and message recipients already carry.
	///
	/// Only the marker is needed: every text column in these tables was already NVARCHAR(MAX) when
	/// the tables were created (M0107, M0112), M0127 widened the one exception
	/// (ModerationRequests.OriginalContentType), and both binary payloads — OriginalContent and
	/// EvidenceContent — are VARBINARY(MAX), which holds an rgdpb envelope as-is.
	///
	/// Additive and inert until the department is enrolled and the catalog-upgrade sweep runs.
	/// </summary>
	[Migration(139)]
	public class M0139_AddModerationProtectionMarkers : Migration
	{
		private static readonly string[] Tables =
		{
			"ModerationRequests",
			"ModerationReports",
			"ModerationActions",
			"ChatMessageFlags",
			"ChatModerationActions",
			"ChatExports"
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
			// The marker only. The values themselves stay exactly as they are: for an enrolled
			// department these columns hold the ONLY copy of the moderated content, and dropping or
			// narrowing them would destroy evidence that exists nowhere else.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("IsProtected").Exists())
					Delete.Column("IsProtected").FromTable(table);
			}
		}
	}
}
