using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(111)]
	public class M0111_CascadeCommunicationTestDeletes : Migration
	{
		public override void Up()
		{
			Delete.ForeignKey("FK_CommunicationTestResults_CommunicationTestRuns")
				.OnTable("CommunicationTestResults");
			Delete.ForeignKey("FK_CommunicationTestRuns_CommunicationTests")
				.OnTable("CommunicationTestRuns");

			Create.ForeignKey("FK_CommunicationTestRuns_CommunicationTests")
				.FromTable("CommunicationTestRuns").ForeignColumn("CommunicationTestId")
				.ToTable("CommunicationTests").PrimaryColumn("CommunicationTestId")
				.OnDelete(Rule.Cascade);

			Create.ForeignKey("FK_CommunicationTestResults_CommunicationTestRuns")
				.FromTable("CommunicationTestResults").ForeignColumn("CommunicationTestRunId")
				.ToTable("CommunicationTestRuns").PrimaryColumn("CommunicationTestRunId")
				.OnDelete(Rule.Cascade);
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_CommunicationTestResults_CommunicationTestRuns")
				.OnTable("CommunicationTestResults");
			Delete.ForeignKey("FK_CommunicationTestRuns_CommunicationTests")
				.OnTable("CommunicationTestRuns");

			Create.ForeignKey("FK_CommunicationTestRuns_CommunicationTests")
				.FromTable("CommunicationTestRuns").ForeignColumn("CommunicationTestId")
				.ToTable("CommunicationTests").PrimaryColumn("CommunicationTestId");

			Create.ForeignKey("FK_CommunicationTestResults_CommunicationTestRuns")
				.FromTable("CommunicationTestResults").ForeignColumn("CommunicationTestRunId")
				.ToTable("CommunicationTestRuns").PrimaryColumn("CommunicationTestRunId");
		}
	}
}
