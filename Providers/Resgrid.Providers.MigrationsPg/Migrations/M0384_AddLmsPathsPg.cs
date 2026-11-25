using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0384 (Training T3 learning paths): lmspaths, lmspathsteps and the lmspathassignments columns that link A1's
	/// ordered-list assignments to their system path. Guarded for safe retry.
	/// </summary>
	[Migration(384)]
	public class M0384_AddLmsPathsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmspaths (
	lmspathid serial NOT NULL,
	departmentid integer NOT NULL,
	systemkey citext NULL,
	name text NOT NULL,
	description text NULL,
	issuescertificationtypeid integer NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmspaths PRIMARY KEY (lmspathid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmspaths_departmentid ON public.lmspaths (departmentid);");
			Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ux_lmspaths_systemkey ON public.lmspaths (systemkey) WHERE systemkey IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmspathsteps (
	lmspathstepid serial NOT NULL,
	lmspathid integer NOT NULL,
	departmentid integer NOT NULL,
	sortorder integer NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	targetrefid integer NOT NULL,
	targetsystemkey citext NULL,
	prerequisiteorders text NULL,
	electivegroup integer NULL,
	electiverequired integer NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmspathsteps PRIMARY KEY (lmspathstepid),
	CONSTRAINT fk_lmspathsteps_lmspaths FOREIGN KEY (lmspathid) REFERENCES public.lmspaths (lmspathid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmspathsteps_path ON public.lmspathsteps (lmspathid, sortorder);");
			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmspathsteps_departmentid ON public.lmspathsteps (departmentid);");

			Execute.Sql(@"ALTER TABLE public.lmspathassignments ADD COLUMN IF NOT EXISTS lmspathid integer NULL;");
			Execute.Sql(@"ALTER TABLE public.lmspathassignments ADD COLUMN IF NOT EXISTS completedon timestamp without time zone NULL;");
			Execute.Sql(@"ALTER TABLE public.lmspathassignments ADD COLUMN IF NOT EXISTS personnelcertificationid integer NULL;");
			Execute.Sql(@"ALTER TABLE public.lmspathassignments ADD COLUMN IF NOT EXISTS lmsrequirementid integer NULL;");
			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmspathassignments_path ON public.lmspathassignments (lmspathid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS public.ix_lmspathassignments_path;");
			Execute.Sql("ALTER TABLE public.lmspathassignments DROP COLUMN IF EXISTS lmsrequirementid;");
			Execute.Sql("ALTER TABLE public.lmspathassignments DROP COLUMN IF EXISTS personnelcertificationid;");
			Execute.Sql("ALTER TABLE public.lmspathassignments DROP COLUMN IF EXISTS completedon;");
			Execute.Sql("ALTER TABLE public.lmspathassignments DROP COLUMN IF EXISTS lmspathid;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmspathsteps;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmspaths;");
		}
	}
}
