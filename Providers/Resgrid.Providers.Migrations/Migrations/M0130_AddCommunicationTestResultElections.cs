using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records, per communication test result row, the member's own notification election for that
	/// channel, the staffing level they were on when the run was built, and whether the department's
	/// Suppress (Mute) Staffing Levels setting muted them. All three are snapshots: a report read
	/// months later has to describe the run as it happened, not as the current profile and current
	/// department settings would have it.
	///
	/// ChannelEnabled is nullable on purpose -- runs built before this migration have no election
	/// recorded, and the report falls back to the live profile for those rather than claiming every
	/// historical channel was switched off.
	/// </summary>
	[Migration(130)]
	public class M0130_AddCommunicationTestResultElections : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CommunicationTestResults").Column("ChannelEnabled").Exists())
			{
				Alter.Table("CommunicationTestResults")
					.AddColumn("ChannelEnabled").AsBoolean().Nullable()
					.AddColumn("StaffingLevel").AsInt32().Nullable()
					.AddColumn("StaffingLevelText").AsString(50).Nullable()
					.AddColumn("Suppressed").AsBoolean().NotNullable().WithDefaultValue(false);
			}
		}

		public override void Down()
		{
			if (Schema.Table("CommunicationTestResults").Column("ChannelEnabled").Exists())
			{
				Delete.Column("Suppressed").FromTable("CommunicationTestResults");
				Delete.Column("StaffingLevelText").FromTable("CommunicationTestResults");
				Delete.Column("StaffingLevel").FromTable("CommunicationTestResults");
				Delete.Column("ChannelEnabled").FromTable("CommunicationTestResults");
			}
		}
	}
}
