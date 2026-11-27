using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0408_AddTrainingSubmissionReview: learningrecords.reviewstate and submittedon; learningrecordfiles.learningrecordid,
	/// sizebytes, sha256, evidencekind and scanstate (3 = Skipped for files stored before scanning); T1 submissions get their state from their
	/// status and their single file is linked. Guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(408)]
	public class M0408_AddTrainingSubmissionReviewPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS reviewstate integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords ADD COLUMN IF NOT EXISTS submittedon timestamp without time zone NULL;");

			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles ADD COLUMN IF NOT EXISTS learningrecordid integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles ADD COLUMN IF NOT EXISTS sizebytes bigint NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles ADD COLUMN IF NOT EXISTS sha256 text NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles ADD COLUMN IF NOT EXISTS evidencekind integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles ADD COLUMN IF NOT EXISTS scanstate integer NOT NULL DEFAULT 3;");

			Execute.Sql(@"
UPDATE public.learningrecordfiles f SET learningrecordid = r.learningrecordid
FROM public.learningrecords r
WHERE r.departmentid = f.departmentid AND r.evidencefileid = f.learningrecordfileid AND f.learningrecordid IS NULL;");
			Execute.Sql("UPDATE public.learningrecordfiles SET sizebytes = octet_length(data) WHERE sizebytes = 0 AND data IS NOT NULL;");
			Execute.Sql(@"
UPDATE public.learningrecords
SET reviewstate = CASE status WHEN 0 THEN 0 WHEN 2 THEN 3 ELSE CASE WHEN verifiedbyuserid IS NULL THEN 4 ELSE 2 END END,
	submittedon = COALESCE(submittedon, createdon)
WHERE sourcekind = 3 AND reviewstate IS NULL AND status IN (0, 1, 2);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecords_review ON public.learningrecords (departmentid, sourcekind, status, reviewstate);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_learningrecordfiles_record ON public.learningrecordfiles (departmentid, learningrecordid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ix_learningrecordfiles_record;");
			Execute.Sql("DROP INDEX IF EXISTS public.ix_learningrecords_review;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles DROP COLUMN IF EXISTS scanstate;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles DROP COLUMN IF EXISTS evidencekind;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles DROP COLUMN IF EXISTS sha256;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles DROP COLUMN IF EXISTS sizebytes;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecordfiles DROP COLUMN IF EXISTS learningrecordid;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS submittedon;");
			Execute.Sql("ALTER TABLE IF EXISTS public.learningrecords DROP COLUMN IF EXISTS reviewstate;");
		}
	}
}
