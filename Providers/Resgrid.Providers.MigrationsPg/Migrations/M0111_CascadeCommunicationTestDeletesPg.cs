using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(111)]
	public class M0111_CascadeCommunicationTestDeletesPg : Migration
	{
		public override void Up()
		{
			Delete.ForeignKey("fk_communicationtestresults_communicationtestruns")
				.OnTable("communicationtestresults");
			Delete.ForeignKey("fk_communicationtestruns_communicationtests")
				.OnTable("communicationtestruns");

			Create.ForeignKey("fk_communicationtestruns_communicationtests")
				.FromTable("communicationtestruns").ForeignColumn("communicationtestid")
				.ToTable("communicationtests").PrimaryColumn("communicationtestid")
				.OnDelete(Rule.Cascade);

			Create.ForeignKey("fk_communicationtestresults_communicationtestruns")
				.FromTable("communicationtestresults").ForeignColumn("communicationtestrunid")
				.ToTable("communicationtestruns").PrimaryColumn("communicationtestrunid")
				.OnDelete(Rule.Cascade);
		}

		public override void Down()
		{
			Delete.ForeignKey("fk_communicationtestresults_communicationtestruns")
				.OnTable("communicationtestresults");
			Delete.ForeignKey("fk_communicationtestruns_communicationtests")
				.OnTable("communicationtestruns");

			Create.ForeignKey("fk_communicationtestruns_communicationtests")
				.FromTable("communicationtestruns").ForeignColumn("communicationtestid")
				.ToTable("communicationtests").PrimaryColumn("communicationtestid");

			Create.ForeignKey("fk_communicationtestresults_communicationtestruns")
				.FromTable("communicationtestresults").ForeignColumn("communicationtestrunid")
				.ToTable("communicationtestruns").PrimaryColumn("communicationtestrunid");
		}
	}
}
