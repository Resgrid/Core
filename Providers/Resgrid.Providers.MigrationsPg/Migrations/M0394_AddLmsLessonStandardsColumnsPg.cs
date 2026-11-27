using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0394_AddLmsLessonStandardsColumns: the Scorm, Cmi5 and LtiLink columns on lmslessons. Guarded; Down drops them.</summary>
	[Migration(394)]
	public class M0394_AddLmsLessonStandardsColumnsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons ADD COLUMN IF NOT EXISTS lmscontentpackageid integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons ADD COLUMN IF NOT EXISTS lmsltitoolid integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons ADD COLUMN IF NOT EXISTS ltitargetlinkuri text NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons ADD COLUMN IF NOT EXISTS lticustomparameters text NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons DROP COLUMN IF EXISTS lticustomparameters;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons DROP COLUMN IF EXISTS ltitargetlinkuri;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons DROP COLUMN IF EXISTS lmsltitoolid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmslessons DROP COLUMN IF EXISTS lmscontentpackageid;");
		}
	}
}
