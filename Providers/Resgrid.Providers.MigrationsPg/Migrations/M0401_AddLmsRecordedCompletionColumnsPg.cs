using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0401_AddLmsRecordedCompletionColumns: lmsenrollments and lmslessonprogress recorded-completion columns. Guarded for safe retry.</summary>
	[Migration(401)]
	public class M0401_AddLmsRecordedCompletionColumnsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsenrollments ADD COLUMN IF NOT EXISTS completionmethod integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsenrollments ADD COLUMN IF NOT EXISTS recordedcompletionid integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessonprogress ADD COLUMN IF NOT EXISTS recordedcompletionid integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessonprogress DROP COLUMN IF EXISTS recordedcompletionid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsenrollments DROP COLUMN IF EXISTS recordedcompletionid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsenrollments DROP COLUMN IF EXISTS completionmethod;");
		}
	}
}
