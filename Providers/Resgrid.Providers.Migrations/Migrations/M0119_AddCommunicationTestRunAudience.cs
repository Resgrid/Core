using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Snapshots the audience a communication test run covers onto the run itself. Targeting is
	/// resolved when the run is started rather than when the worker builds it, so editing a test's
	/// targets while its run sits on the queue can no longer change who that run tests. NULL means
	/// the run predates the snapshot and the builder falls back to the test's current targeting.
	/// </summary>
	[Migration(119)]
	public class M0119_AddCommunicationTestRunAudience : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CommunicationTestRuns").Column("TargetedUserIds").Exists())
			{
				Alter.Table("CommunicationTestRuns")
					.AddColumn("TargetedUserIds").AsString(int.MaxValue).Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("CommunicationTestRuns").Column("TargetedUserIds").Exists())
			{
				Delete.Column("TargetedUserIds").FromTable("CommunicationTestRuns");
			}
		}
	}
}
