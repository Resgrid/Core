using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0387_AddLmsTrainingPlans: the same tables and indexes in lower case, text columns citext. Guarded for safe retry.
	/// </summary>
	[Migration(387)]
	public class M0387_AddLmsTrainingPlansPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstrainingplans (
	lmstrainingplanid serial NOT NULL,
	departmentid integer NOT NULL,
	year integer NOT NULL,
	name citext NOT NULL,
	scopekind integer NOT NULL DEFAULT 0,
	scopeid integer NULL,
	status integer NOT NULL DEFAULT 0,
	notes citext NULL,
	adoptedon timestamp without time zone NULL,
	adoptedbyuserid citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmstrainingplans PRIMARY KEY (lmstrainingplanid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingplans_department_year ON public.lmstrainingplans (departmentid, year);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstrainingplanitems (
	lmstrainingplanitemid serial NOT NULL,
	lmstrainingplanid integer NOT NULL,
	departmentid integer NOT NULL,
	month integer NOT NULL,
	categoryid integer NULL,
	title citext NOT NULL,
	plannedhours numeric(12,2) NOT NULL DEFAULT 0,
	plannedsessions integer NOT NULL DEFAULT 1,
	plannedon timestamp without time zone NULL,
	notes citext NULL,
	calendaritemid integer NULL,
	classsessionid integer NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmstrainingplanitems PRIMARY KEY (lmstrainingplanitemid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingplanitems_plan ON public.lmstrainingplanitems (lmstrainingplanid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingplanitems_departmentid ON public.lmstrainingplanitems (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmstrainingplanitems;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmstrainingplans;");
		}
	}
}
