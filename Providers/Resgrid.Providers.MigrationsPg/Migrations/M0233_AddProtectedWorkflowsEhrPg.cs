using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Protected Workflows for EHR integration (see the SQL Server M0233): calls.subjectidentifiers (protected, catalog 29),
	/// calls.part2consentonfile, udffields.sensitivity and workflowcredentials.publicjwks. Constant defaults, so no rewrite.
	/// </summary>
	[Migration(233)]
	public class M0233_AddProtectedWorkflowsEhrPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
ALTER TABLE calls ADD COLUMN IF NOT EXISTS subjectidentifiers citext NULL;
ALTER TABLE calls ADD COLUMN IF NOT EXISTS part2consentonfile boolean NOT NULL DEFAULT false;
ALTER TABLE udffields ADD COLUMN IF NOT EXISTS sensitivity integer NOT NULL DEFAULT 0;
ALTER TABLE workflowcredentials ADD COLUMN IF NOT EXISTS publicjwks citext NULL;");
		}

		public override void Down()
		{
			Execute.Sql(@"
ALTER TABLE workflowcredentials DROP COLUMN IF EXISTS publicjwks;
ALTER TABLE udffields DROP COLUMN IF EXISTS sensitivity;
ALTER TABLE calls DROP COLUMN IF EXISTS part2consentonfile;
ALTER TABLE calls DROP COLUMN IF EXISTS subjectidentifiers;");
		}
	}
}
