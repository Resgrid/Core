using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0389_AddLmsTrainingRequests: the same tables and indexes in lower case, text columns citext. Guarded for safe retry.
	/// </summary>
	[Migration(389)]
	public class M0389_AddLmsTrainingRequestsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstrainingrequests (
	lmstrainingrequestid serial NOT NULL,
	departmentid integer NOT NULL,
	userid citext NOT NULL,
	title citext NOT NULL,
	provider citext NULL,
	location citext NULL,
	coursecode citext NULL,
	startson timestamp without time zone NULL,
	endson timestamp without time zone NULL,
	hours numeric(12,2) NOT NULL DEFAULT 0,
	categoryid integer NULL,
	costtuition numeric(12,2) NOT NULL DEFAULT 0,
	costtravel numeric(12,2) NOT NULL DEFAULT 0,
	costother numeric(12,2) NOT NULL DEFAULT 0,
	currency citext NULL,
	justification citext NULL,
	status integer NOT NULL DEFAULT 0,
	currentstep integer NULL,
	steps citext NULL,
	submittedon timestamp without time zone NOT NULL,
	decidedon timestamp without time zone NULL,
	decidedbyuserid citext NULL,
	learningrecordid integer NULL,
	completedon timestamp without time zone NULL,
	actualcost numeric(12,2) NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmstrainingrequests PRIMARY KEY (lmstrainingrequestid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingrequests_department_status ON public.lmstrainingrequests (departmentid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingrequests_department_user ON public.lmstrainingrequests (departmentid, userid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstrainingrequestdecisions (
	lmstrainingrequestdecisionid serial NOT NULL,
	lmstrainingrequestid integer NOT NULL,
	departmentid integer NOT NULL,
	step integer NOT NULL,
	decision integer NOT NULL,
	decidedbyuserid citext NULL,
	note citext NULL,
	decidedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmstrainingrequestdecisions PRIMARY KEY (lmstrainingrequestdecisionid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingrequestdecisions_request ON public.lmstrainingrequestdecisions (lmstrainingrequestid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingrequestdecisions_departmentid ON public.lmstrainingrequestdecisions (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmstrainingrequestdecisions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmstrainingrequests;");
		}
	}
}
