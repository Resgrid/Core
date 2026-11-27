using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0402_AddLmsLearnerNotices: lmslearnernotices and the language, reminder opt-out and unsubscribe key of
	/// lmsexternallearners, in lower case. The unsubscribe key is text (an exact token, never citext). Guarded for safe retry.
	/// </summary>
	[Migration(402)]
	public class M0402_AddLmsLearnerNoticesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslearnernotices (
	lmslearnernoticeid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsexternallearnerid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	kind integer NOT NULL,
	noticekey text NOT NULL,
	claimedon timestamp without time zone NOT NULL,
	senton timestamp without time zone NULL,
	skipped text NULL,
	CONSTRAINT pk_lmslearnernotices PRIMARY KEY (lmslearnernoticeid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmslearnernotices_key ON public.lmslearnernotices (departmentid, lmsexternallearnerid, lmsclasssessionid, kind, noticekey);");

			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners ADD COLUMN IF NOT EXISTS language citext NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners ADD COLUMN IF NOT EXISTS remindersoptout boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners ADD COLUMN IF NOT EXISTS unsubscribekey text NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners DROP COLUMN IF EXISTS unsubscribekey;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners DROP COLUMN IF EXISTS remindersoptout;");
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsexternallearners DROP COLUMN IF EXISTS language;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmslearnernotices;");
		}
	}
}
