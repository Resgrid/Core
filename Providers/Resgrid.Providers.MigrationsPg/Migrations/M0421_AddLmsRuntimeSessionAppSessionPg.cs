using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0421_AddLmsRuntimeSessionAppSession (placeholder number, renumbered at merge): lmsruntimesessions.appsessionid
	/// (integer, null) and its partial index. Guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(421)]
	public class M0421_AddLmsRuntimeSessionAppSessionPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsruntimesessions ADD COLUMN IF NOT EXISTS appsessionid integer NULL;");
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsruntimesessions' AND column_name = 'appsessionid') THEN
		CREATE INDEX IF NOT EXISTS ix_lmsruntimesessions_appsession ON public.lmsruntimesessions (appsessionid, lmsruntimesessionid) WHERE appsessionid IS NOT NULL;
	END IF;
END $$;");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ix_lmsruntimesessions_appsession;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsruntimesessions DROP COLUMN IF EXISTS appsessionid;");
		}
	}
}
