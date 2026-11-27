using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0412_AddLmsExternalLearnerRowVersion: lmsexternallearners.rowversion (bigint, 0). Guarded for safe retry.</summary>
	[Migration(412)]
	public class M0412_AddLmsExternalLearnerRowVersionPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners ADD COLUMN IF NOT EXISTS rowversion bigint NOT NULL DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners DROP COLUMN IF EXISTS rowversion;");
		}
	}
}
