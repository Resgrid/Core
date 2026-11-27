using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0411_AddLmsExternalLearnerTokenStampHash: lmsexternallearnertokens.stamphash (an exact hash, text). Guarded for safe retry.</summary>
	[Migration(411)]
	public class M0411_AddLmsExternalLearnerTokenStampHashPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearnertokens ADD COLUMN IF NOT EXISTS stamphash text NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearnertokens DROP COLUMN IF EXISTS stamphash;");
		}
	}
}
