using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0409_AddLearningRecordReviewEvents: learningrecordreviewevents in lower case, member and actor ids citext. Guarded
	/// for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(409)]
	public class M0409_AddLearningRecordReviewEventsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.learningrecordreviewevents (
	learningrecordrevieweventid serial NOT NULL,
	departmentid integer NOT NULL,
	learningrecordid integer NOT NULL,
	userid citext NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	actoruserid citext NULL,
	note text NULL,
	hours numeric(9, 2) NULL,
	filecount integer NOT NULL DEFAULT 0,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_learningrecordreviewevents PRIMARY KEY (learningrecordrevieweventid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordreviewevents_record ON public.learningrecordreviewevents (departmentid, learningrecordid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordreviewevents_user ON public.learningrecordreviewevents (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.learningrecordreviewevents;");
		}
	}
}
