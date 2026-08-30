using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Row markers for the moderation family entering the protected-field catalog (ADP plan
	/// section 5.3). isprotected says "this row's cataloged values carry rgdp envelopes" — the same
	/// marker calls' children, unit states, member data and message recipients already carry.
	///
	/// Only the marker is needed: every text column in these tables is citext (unbounded) from the
	/// migrations that created them (M0107, M0112) and both binary payloads — originalcontent and
	/// evidencecontent — are bytea, which holds an rgdpb envelope as-is.
	///
	/// Additive and inert until the department is enrolled and the catalog-upgrade sweep runs.
	/// </summary>
	[Migration(139)]
	public class M0139_AddModerationProtectionMarkersPg : Migration
	{
		private static readonly string[] Tables =
		{
			"moderationrequests",
			"moderationreports",
			"moderationactions",
			"chatmessageflags",
			"chatmoderationactions",
			"chatexports"
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
			// The marker only. The values themselves stay exactly as they are: for an enrolled
			// department these columns hold the ONLY copy of the moderated content, and dropping or
			// narrowing them would destroy evidence that exists nowhere else.
			foreach (var table in Tables)
			{
				if (Schema.Table(table).Column("isprotected").Exists())
					Delete.Column("isprotected").FromTable(table);
			}
		}
	}
}
