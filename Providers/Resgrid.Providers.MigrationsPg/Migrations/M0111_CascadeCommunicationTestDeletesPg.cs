using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// Foreign keys are added without scanning existing rows while ACCESS EXCLUSIVE is held.
	// TransactionBehavior.None makes each Execute.Sql call self-commit, so validation runs later.
	[Migration(111, TransactionBehavior.None)]
	public class M0111_CascadeCommunicationTestDeletesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"ALTER TABLE communicationtestresults
				DROP CONSTRAINT IF EXISTS fk_communicationtestresults_communicationtestruns;
			ALTER TABLE communicationtestresults
				ADD CONSTRAINT fk_communicationtestresults_communicationtestruns
				FOREIGN KEY (communicationtestrunid) REFERENCES communicationtestruns (communicationtestrunid)
				ON DELETE CASCADE NOT VALID;");
			Execute.Sql(@"ALTER TABLE communicationtestresults
				VALIDATE CONSTRAINT fk_communicationtestresults_communicationtestruns;");

			Execute.Sql(@"ALTER TABLE communicationtestruns
				DROP CONSTRAINT IF EXISTS fk_communicationtestruns_communicationtests;
			ALTER TABLE communicationtestruns
				ADD CONSTRAINT fk_communicationtestruns_communicationtests
				FOREIGN KEY (communicationtestid) REFERENCES communicationtests (communicationtestid)
				ON DELETE CASCADE NOT VALID;");
			Execute.Sql(@"ALTER TABLE communicationtestruns
				VALIDATE CONSTRAINT fk_communicationtestruns_communicationtests;");
		}

		public override void Down()
		{
			Execute.Sql(@"ALTER TABLE communicationtestruns
				DROP CONSTRAINT IF EXISTS fk_communicationtestruns_communicationtests;
			ALTER TABLE communicationtestruns
				ADD CONSTRAINT fk_communicationtestruns_communicationtests
				FOREIGN KEY (communicationtestid) REFERENCES communicationtests (communicationtestid)
				NOT VALID;");
			Execute.Sql(@"ALTER TABLE communicationtestruns
				VALIDATE CONSTRAINT fk_communicationtestruns_communicationtests;");

			Execute.Sql(@"ALTER TABLE communicationtestresults
				DROP CONSTRAINT IF EXISTS fk_communicationtestresults_communicationtestruns;
			ALTER TABLE communicationtestresults
				ADD CONSTRAINT fk_communicationtestresults_communicationtestruns
				FOREIGN KEY (communicationtestrunid) REFERENCES communicationtestruns (communicationtestrunid)
				NOT VALID;");
			Execute.Sql(@"ALTER TABLE communicationtestresults
				VALIDATE CONSTRAINT fk_communicationtestresults_communicationtestruns;");
		}
	}
}
