using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0410_AddTrainingReviewDigests: trainingreviewdigests in lower case with the unique (departmentid, digestdate) claim.
	/// Guarded for safe retry; Down removes exactly what Up added.
	/// </summary>
	[Migration(410)]
	public class M0410_AddTrainingReviewDigestsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.trainingreviewdigests (
	trainingreviewdigestid serial NOT NULL,
	departmentid integer NOT NULL,
	digestdate timestamp without time zone NOT NULL,
	reviewers integer NOT NULL DEFAULT 0,
	submissions integer NOT NULL DEFAULT 0,
	senton timestamp without time zone NOT NULL,
	CONSTRAINT pk_trainingreviewdigests PRIMARY KEY (trainingreviewdigestid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_trainingreviewdigests_day ON public.trainingreviewdigests (departmentid, digestdate);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.trainingreviewdigests;");
		}
	}
}
