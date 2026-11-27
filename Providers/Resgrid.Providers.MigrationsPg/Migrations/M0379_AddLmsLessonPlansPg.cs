using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0379_AddLmsLessonPlans: the same tables and indexes in lower case (content JSON as text). Guarded for safe retry.</summary>
	[Migration(379)]
	public class M0379_AddLmsLessonPlansPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslessonplans (
	lmslessonplanid serial NOT NULL,
	departmentid integer NOT NULL,
	title citext NOT NULL,
	starterkey citext NULL,
	currentversionid integer NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmslessonplans PRIMARY KEY (lmslessonplanid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmslessonplans_departmentid ON public.lmslessonplans (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslessonplanversions (
	lmslessonplanversionid serial NOT NULL,
	lmslessonplanid integer NOT NULL,
	departmentid integer NOT NULL,
	status integer NOT NULL DEFAULT 0,
	major integer NOT NULL DEFAULT 0,
	minor integer NOT NULL DEFAULT 0,
	title citext NOT NULL,
	contentjson text NULL,
	contenthash citext NULL,
	changesummary citext NULL,
	submittedon timestamp without time zone NULL,
	submittedbyuserid citext NULL,
	decidedon timestamp without time zone NULL,
	decidedbyuserid citext NULL,
	decisionnote citext NULL,
	publishedon timestamp without time zone NULL,
	publishedbyuserid citext NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmslessonplanversions PRIMARY KEY (lmslessonplanversionid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmslessonplanversions_plan ON public.lmslessonplanversions (departmentid, lmslessonplanid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmslessonplanversions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmslessonplans;");
		}
	}
}
