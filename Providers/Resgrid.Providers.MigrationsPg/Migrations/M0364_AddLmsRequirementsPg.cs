using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0364 (Training T3 learning requirements): lmsrequirements, lmsrequirementaudiences and lmsrequirementmembers,
	/// with the filtered unique index that keeps one open row per member and requirement. Guarded for safe retry.
	/// </summary>
	[Migration(364)]
	public class M0364_AddLmsRequirementsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsrequirements (
	lmsrequirementid serial NOT NULL,
	departmentid integer NOT NULL,
	name text NOT NULL,
	description text NULL,
	kind integer NOT NULL DEFAULT 0,
	targetrefid integer NULL,
	targetcode citext NULL,
	requiredhours numeric(9,2) NULL,
	requiredcount integer NULL,
	recurrence integer NOT NULL DEFAULT 0,
	intervalmonths integer NULL,
	duedays integer NOT NULL DEFAULT 30,
	gracedays integer NOT NULL DEFAULT 0,
	atriskdays integer NOT NULL DEFAULT 14,
	newmembersonly boolean NOT NULL DEFAULT false,
	autoassign boolean NOT NULL DEFAULT false,
	isonboarding boolean NOT NULL DEFAULT false,
	backscertificationtypeid integer NULL,
	enforcementenabled boolean NOT NULL DEFAULT false,
	alertsupervisors boolean NOT NULL DEFAULT false,
	alertuserids text NULL,
	alertoncompletion boolean NOT NULL DEFAULT false,
	isactive boolean NOT NULL DEFAULT true,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	deletedon timestamp without time zone NULL,
	deletedbyuserid citext NULL,
	CONSTRAINT pk_lmsrequirements PRIMARY KEY (lmsrequirementid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirements_departmentid ON public.lmsrequirements (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsrequirementaudiences (
	lmsrequirementaudienceid serial NOT NULL,
	lmsrequirementid integer NOT NULL,
	departmentid integer NOT NULL,
	kind integer NOT NULL,
	refid citext NULL,
	CONSTRAINT pk_lmsrequirementaudiences PRIMARY KEY (lmsrequirementaudienceid),
	CONSTRAINT fk_lmsrequirementaudiences_lmsrequirements FOREIGN KEY (lmsrequirementid) REFERENCES public.lmsrequirements (lmsrequirementid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementaudiences_requirement ON public.lmsrequirementaudiences (lmsrequirementid);");
			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementaudiences_departmentid ON public.lmsrequirementaudiences (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsrequirementmembers (
	lmsrequirementmemberid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsrequirementid integer NOT NULL,
	userid citext NOT NULL,
	enteredon timestamp without time zone NOT NULL,
	lefton timestamp without time zone NULL,
	notrequired boolean NOT NULL DEFAULT false,
	state integer NOT NULL DEFAULT 1,
	statechangedon timestamp without time zone NULL,
	dueon timestamp without time zone NULL,
	lastcompletedon timestamp without time zone NULL,
	exempton timestamp without time zone NULL,
	exemptuntil timestamp without time zone NULL,
	exemptreason text NULL,
	exemptbyuserid citext NULL,
	extendeddueon timestamp without time zone NULL,
	extendedbyuserid citext NULL,
	extensionreason text NULL,
	atrisknotifiedfor timestamp without time zone NULL,
	overduenotifiedfor timestamp without time zone NULL,
	assignedfordueon timestamp without time zone NULL,
	CONSTRAINT pk_lmsrequirementmembers PRIMARY KEY (lmsrequirementmemberid)
);");

			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementmembers_department_requirement ON public.lmsrequirementmembers (departmentid, lmsrequirementid);");
			Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_lmsrequirementmembers_department_user ON public.lmsrequirementmembers (departmentid, userid);");
			Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsrequirementmembers_open ON public.lmsrequirementmembers (lmsrequirementid, userid) WHERE lefton IS NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsrequirementmembers;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsrequirementaudiences;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsrequirements;");
		}
	}
}
