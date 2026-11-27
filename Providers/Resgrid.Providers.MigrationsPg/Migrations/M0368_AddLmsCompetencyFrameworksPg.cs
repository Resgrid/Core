using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0368_AddLmsCompetencyFrameworks: the same tables and indexes in lower case, text columns citext. Guarded for safe retry.
	/// </summary>
	[Migration(368)]
	public class M0368_AddLmsCompetencyFrameworksPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscompetencyframeworks (
	lmscompetencyframeworkid serial NOT NULL,
	departmentid integer NOT NULL,
	catalogkey citext NULL,
	name citext NOT NULL,
	publisher citext NULL,
	edition citext NULL,
	description citext NULL,
	source integer NOT NULL DEFAULT 0,
	casedocumentid citext NULL,
	caseuri citext NULL,
	evidencemaxagemonths integer NOT NULL DEFAULT 0,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmscompetencyframeworks PRIMARY KEY (lmscompetencyframeworkid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscompetencyframeworks_departmentid ON public.lmscompetencyframeworks (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscompetencyitems (
	lmscompetencyitemid serial NOT NULL,
	lmscompetencyframeworkid integer NOT NULL,
	departmentid integer NOT NULL,
	parentitemid integer NULL,
	identifier citext NULL,
	humancodingscheme citext NULL,
	label citext NULL,
	itemtype citext NULL,
	sortorder integer NOT NULL DEFAULT 0,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmscompetencyitems PRIMARY KEY (lmscompetencyitemid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscompetencyitems_framework ON public.lmscompetencyitems (lmscompetencyframeworkid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscompetencyitems_departmentid ON public.lmscompetencyitems (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscompetencyalignments (
	lmscompetencyalignmentid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscompetencyitemid integer NOT NULL,
	subjectkind integer NOT NULL,
	subjectid citext NOT NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmscompetencyalignments PRIMARY KEY (lmscompetencyalignmentid)
);");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscompetencyalignments_item_subject ON public.lmscompetencyalignments (departmentid, lmscompetencyitemid, subjectkind, subjectid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscompetencyalignments_subject ON public.lmscompetencyalignments (departmentid, subjectkind, subjectid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmscompetencyalignments;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscompetencyitems;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscompetencyframeworks;");
		}
	}
}
