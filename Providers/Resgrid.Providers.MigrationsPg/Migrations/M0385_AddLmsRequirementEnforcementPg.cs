using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0385 (Training T3 training-backed enforcement and worker 93's clock): lmsrequirementenforcements and
	/// lmsrequirementdepartmentstates. Guarded for safe retry.
	/// </summary>
	[Migration(385)]
	public class M0385_AddLmsRequirementEnforcementPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsrequirementenforcements (
	lmsrequirementenforcementid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsrequirementid integer NOT NULL,
	lmsrequirementmemberid integer NOT NULL,
	userid citext NOT NULL,
	personnelcertificationid integer NOT NULL,
	departmentcertificationtypeid integer NOT NULL,
	previousstatus integer NOT NULL,
	previousexpireson timestamp without time zone NULL,
	causelearningrecordid integer NULL,
	dueon timestamp without time zone NOT NULL,
	actedon timestamp without time zone NOT NULL,
	rolesjson text NULL,
	undoneon timestamp without time zone NULL,
	undoreason integer NULL,
	restoredrolesjson text NULL,
	CONSTRAINT pk_lmsrequirementenforcements PRIMARY KEY (lmsrequirementenforcementid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementenforcements_department_user ON public.lmsrequirementenforcements (departmentid, userid);");
			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementenforcements_open ON public.lmsrequirementenforcements (departmentid) WHERE undoneon IS NULL;");
			Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsrequirementenforcements_openaction ON public.lmsrequirementenforcements (lmsrequirementid, userid) WHERE undoneon IS NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsrequirementdepartmentstates (
	lmsrequirementdepartmentstateid serial NOT NULL,
	departmentid integer NOT NULL,
	lastsweeplocaldate timestamp without time zone NULL,
	lastsweepon timestamp without time zone NULL,
	lastrefusedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsrequirementdepartmentstates PRIMARY KEY (lmsrequirementdepartmentstateid)
);");

			Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsrequirementdepartmentstates_departmentid ON public.lmsrequirementdepartmentstates (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsrequirementdepartmentstates;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsrequirementenforcements;");
		}
	}
}
