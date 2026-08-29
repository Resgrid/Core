using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records, per communication test result row, the member's own notification election for that
	/// channel, the staffing level they were on when the run was built, and whether the department's
	/// Suppress (Mute) Staffing Levels setting muted them. All three are snapshots: a report read
	/// months later has to describe the run as it happened, not as the current profile and current
	/// department settings would have it.
	///
	/// channelenabled is nullable on purpose -- runs built before this migration have no election
	/// recorded, and the report falls back to the live profile for those rather than claiming every
	/// historical channel was switched off. staffingleveltext is citext to match the other
	/// communication test text columns (M0062).
	/// </summary>
	[Migration(130)]
	public class M0130_AddCommunicationTestResultElectionsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("communicationtestresults").Column("channelenabled").Exists())
			{
				Alter.Table("communicationtestresults")
					.AddColumn("channelenabled").AsBoolean().Nullable()
					.AddColumn("staffinglevel").AsInt32().Nullable()
					.AddColumn("staffingleveltext").AsCustom("citext").Nullable()
					.AddColumn("suppressed").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("communicationtestresults").Column("channelenabled").Exists())
			{
				Delete.Column("suppressed").FromTable("communicationtestresults");
				Delete.Column("staffingleveltext").FromTable("communicationtestresults");
				Delete.Column("staffinglevel").FromTable("communicationtestresults");
				Delete.Column("channelenabled").FromTable("communicationtestresults");
			}
		}
	}
}
