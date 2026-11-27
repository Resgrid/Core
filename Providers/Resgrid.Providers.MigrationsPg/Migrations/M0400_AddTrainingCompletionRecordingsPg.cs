using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0400_AddTrainingCompletionRecordings: trainingcompletionrecordings, and the completion metadata on learningrecords
	/// and trainingusers, in lower case. User ids are citext. Guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(400)]
	public class M0400_AddTrainingCompletionRecordingsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.trainingcompletionrecordings (
	trainingcompletionrecordingid serial NOT NULL,
	departmentid integer NOT NULL,
	userid citext NOT NULL,
	targetkind integer NOT NULL DEFAULT 0,
	targetid integer NOT NULL,
	targetversionid integer NULL,
	targetname citext NULL,
	sourcekind integer NOT NULL DEFAULT 0,
	sourcerefid citext NULL,
	batchkey text NULL,
	note text NULL,
	attestationversion text NOT NULL,
	quizwaived boolean NOT NULL DEFAULT false,
	completedon timestamp without time zone NOT NULL,
	traininguserid integer NULL,
	cycle integer NULL,
	enrollmentid integer NULL,
	learningrecordid integer NULL,
	personnelcertificationid integer NULL,
	recordedbyuserid citext NOT NULL,
	recordedon timestamp without time zone NOT NULL,
	voidedon timestamp without time zone NULL,
	voidedbyuserid citext NULL,
	voidreason text NULL,
	CONSTRAINT pk_trainingcompletionrecordings PRIMARY KEY (trainingcompletionrecordingid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcompletionrecordings_user ON public.trainingcompletionrecordings (departmentid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcompletionrecordings_target ON public.trainingcompletionrecordings (departmentid, targetkind, targetid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_trainingcompletionrecordings_source ON public.trainingcompletionrecordings (departmentid, sourcekind, sourcerefid);");

			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS completionmethod integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS recordedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS recordedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS recordedcompletionid integer NULL;");

			Execute.Sql("ALTER TABLE IF EXISTS public.trainingusers ADD COLUMN IF NOT EXISTS completionmethod integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.trainingusers ADD COLUMN IF NOT EXISTS recordedcompletionid integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.trainingusers DROP COLUMN IF EXISTS recordedcompletionid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.trainingusers DROP COLUMN IF EXISTS completionmethod;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS recordedcompletionid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS recordedon;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS recordedbyuserid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS completionmethod;");
			Execute.Sql("DROP TABLE IF EXISTS public.trainingcompletionrecordings;");
		}
	}
}
