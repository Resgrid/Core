using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0384_AddLmsSkillNotifications: the same tables and indexes in lower case, member ids and text citext (hashes and the idempotency
	/// key stay exact). Guarded for safe retry.
	/// </summary>
	[Migration(384)]
	public class M0384_AddLmsSkillNotificationsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillnotifications (
	lmsskillnotificationid serial NOT NULL,
	departmentid integer NOT NULL,
	userid citext NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	text citext NULL,
	createdon timestamp without time zone NOT NULL,
	dueon timestamp without time zone NOT NULL,
	claimedon timestamp without time zone NULL,
	senton timestamp without time zone NULL,
	skipped boolean NOT NULL DEFAULT false,
	CONSTRAINT pk_lmsskillnotifications PRIMARY KEY (lmsskillnotificationid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillnotifications_due ON public.lmsskillnotifications (dueon) WHERE senton IS NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillnotifications_department_user ON public.lmsskillnotifications (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillnotifications;");
		}
	}
}
