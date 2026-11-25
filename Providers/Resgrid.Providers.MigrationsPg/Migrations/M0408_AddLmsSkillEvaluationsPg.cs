using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0408_AddLmsSkillEvaluations: the same tables and indexes in lower case, member ids and text citext (hashes and the idempotency
	/// key stay exact). Guarded for safe retry.
	/// </summary>
	[Migration(408)]
	public class M0408_AddLmsSkillEvaluationsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillevaluations (
	lmsskillevaluationid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsskillsheetid integer NULL,
	lmsskillsheetversionid integer NULL,
	title citext NULL,
	code citext NULL,
	mode integer NOT NULL DEFAULT 0,
	status integer NOT NULL DEFAULT 0,
	result integer NOT NULL DEFAULT 0,
	score numeric(7,2) NULL,
	criticalfailed boolean NOT NULL DEFAULT false,
	timeexceeded boolean NOT NULL DEFAULT false,
	evaluatoruserid citext NULL,
	createdbyuserid citext NULL,
	startedon timestamp without time zone NOT NULL,
	submittedon timestamp without time zone NULL,
	completedon timestamp without time zone NULL,
	durationseconds integer NOT NULL DEFAULT 0,
	answersjson text NULL,
	notes citext NULL,
	callid integer NULL,
	deploymentid integer NULL,
	location citext NULL,
	lmstaskbookid integer NULL,
	taskkey citext NULL,
	classsessionref citext NULL,
	parentevaluationid integer NULL,
	idempotencykey varchar(100) NULL,
	contenthash varchar(64) NULL,
	signaturerequestid integer NULL,
	nextsigneruserid citext NULL,
	externalevaluatorname citext NULL,
	voidedon timestamp without time zone NULL,
	voidedbyuserid citext NULL,
	voidreason citext NULL,
	updatedon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsskillevaluations PRIMARY KEY (lmsskillevaluationid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluations_department_started ON public.lmsskillevaluations (departmentid, startedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluations_evaluator ON public.lmsskillevaluations (departmentid, evaluatoruserid, status);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluations_taskbook ON public.lmsskillevaluations (departmentid, lmstaskbookid) WHERE lmstaskbookid IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluations_request ON public.lmsskillevaluations (departmentid, signaturerequestid) WHERE signaturerequestid IS NOT NULL;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsskillevaluations_idempotency ON public.lmsskillevaluations (departmentid, idempotencykey) WHERE idempotencykey IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillevaluationmembers (
	lmsskillevaluationmemberid serial NOT NULL,
	lmsskillevaluationid integer NOT NULL,
	departmentid integer NOT NULL,
	userid citext NOT NULL,
	state integer NOT NULL DEFAULT 0,
	attemptnumber integer NOT NULL DEFAULT 1,
	splittoevaluationid integer NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsskillevaluationmembers PRIMARY KEY (lmsskillevaluationmemberid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluationmembers_evaluation ON public.lmsskillevaluationmembers (lmsskillevaluationid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluationmembers_user ON public.lmsskillevaluationmembers (departmentid, userid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillevaluationfiles (
	lmsskillevaluationfileid serial NOT NULL,
	lmsskillevaluationid integer NOT NULL,
	departmentid integer NOT NULL,
	stepkey citext NULL,
	filename citext NULL,
	contenttype citext NULL,
	size integer NOT NULL DEFAULT 0,
	sha256 varchar(64) NULL,
	data bytea NULL,
	uploadedbyuserid citext NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsskillevaluationfiles PRIMARY KEY (lmsskillevaluationfileid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillevaluationfiles_evaluation ON public.lmsskillevaluationfiles (departmentid, lmsskillevaluationid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillevaluationfiles;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillevaluationmembers;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillevaluations;");
		}
	}
}
