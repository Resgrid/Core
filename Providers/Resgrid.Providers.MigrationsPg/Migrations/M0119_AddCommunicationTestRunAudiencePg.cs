using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Snapshots the audience a communication test run covers onto the run itself. Targeting is
	/// resolved when the run is started rather than when the worker builds it, so editing a test's
	/// targets while its run sits on the queue can no longer change who that run tests. NULL means
	/// the run predates the snapshot and the builder falls back to the test's current targeting.
	/// citext to match the existing communication test tables (M0062), and unbounded so a large
	/// targeted audience is not truncated.
	/// </summary>
	[Migration(119)]
	public class M0119_AddCommunicationTestRunAudiencePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("communicationtestruns").Column("targeteduserids").Exists())
			{
				Alter.Table("communicationtestruns")
					.AddColumn("targeteduserids").AsCustom("citext").Nullable();
			}
		}

		public override void Down()
		{
			if (Schema.Table("communicationtestruns").Column("targeteduserids").Exists())
			{
				Delete.Column("targeteduserids").FromTable("communicationtestruns");
			}
		}
	}
}
