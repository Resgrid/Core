using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0392_AddLmsContentPackagesAndRuntime: the same tables and indexes in lower case. Member ids and titles are citext;
	/// token hashes, statement ids, registration keys, activity and state ids stay exact (IRIs and tokens are case-sensitive). Guarded for
	/// safe retry.
	/// </summary>
	[Migration(392)]
	public class M0392_AddLmsContentPackagesAndRuntimePg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscontentpackages (
	lmscontentpackageid serial NOT NULL,
	departmentid integer NOT NULL,
	standard integer NOT NULL DEFAULT 0,
	title citext NULL,
	identifier text NULL,
	schemaversion citext NULL,
	filename citext NULL,
	sha256 varchar(64) NULL,
	status integer NOT NULL DEFAULT 0,
	sourcekey text NULL,
	sourcebytes bigint NOT NULL DEFAULT 0,
	contentprefix text NULL,
	filecount integer NOT NULL DEFAULT 0,
	contentbytes bigint NOT NULL DEFAULT 0,
	manifestjson text NULL,
	attempts integer NOT NULL DEFAULT 0,
	lasterror text NULL,
	claimedon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	readyon timestamp without time zone NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	deletedon timestamp without time zone NULL,
	deletedbyuserid citext NULL,
	CONSTRAINT pk_lmscontentpackages PRIMARY KEY (lmscontentpackageid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscontentpackages_department ON public.lmscontentpackages (departmentid, isdeleted);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscontentpackages_queue ON public.lmscontentpackages (status, createdon) WHERE status IN (0, 1);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsruntimeregistrations (
	lmsruntimeregistrationid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsenrollmentid integer NOT NULL,
	lmslessonid integer NOT NULL,
	userid citext NOT NULL,
	kind integer NOT NULL,
	lmscontentpackageid integer NULL,
	lmsltitoolid integer NULL,
	registrationkey varchar(100) NULL,
	statejson text NULL,
	completionstatus varchar(32) NULL,
	successstatus varchar(32) NULL,
	scorepercent numeric(9,2) NULL,
	totalseconds integer NOT NULL DEFAULT 0,
	launchcount integer NOT NULL DEFAULT 0,
	satisfied boolean NOT NULL DEFAULT false,
	satisfiedon timestamp without time zone NULL,
	lastresulton timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	updatedon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsruntimeregistrations PRIMARY KEY (lmsruntimeregistrationid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsruntimeregistrations_enrollment_lesson ON public.lmsruntimeregistrations (departmentid, lmsenrollmentid, lmslessonid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsruntimeregistrations_lesson_key ON public.lmsruntimeregistrations (departmentid, lmslessonid, registrationkey);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsruntimesessions (
	lmsruntimesessionid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsruntimeregistrationid integer NOT NULL,
	lmsenrollmentid integer NOT NULL,
	lmslessonid integer NOT NULL,
	userid citext NOT NULL,
	kind integer NOT NULL,
	itemidentifier text NULL,
	sessionkey varchar(64) NULL,
	tokenhash varchar(64) NOT NULL,
	fetchtokenhash varchar(64) NULL,
	authtokenhash varchar(64) NULL,
	fetchedon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	expireson timestamp without time zone NOT NULL,
	usedon timestamp without time zone NULL,
	endedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsruntimesessions PRIMARY KEY (lmsruntimesessionid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsruntimesessions_token ON public.lmsruntimesessions (tokenhash);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsruntimesessions_fetch ON public.lmsruntimesessions (fetchtokenhash) WHERE fetchtokenhash IS NOT NULL;");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsruntimesessions_auth ON public.lmsruntimesessions (authtokenhash) WHERE authtokenhash IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsruntimesessions_expires ON public.lmsruntimesessions (expireson);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsruntimesessions_department ON public.lmsruntimesessions (departmentid, lmsruntimeregistrationid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscmi5statements (
	lmscmi5statementid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsruntimeregistrationid integer NOT NULL,
	lmsruntimesessionid integer NOT NULL,
	statementid varchar(36) NOT NULL,
	verbid text NULL,
	statementjson text NULL,
	storedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmscmi5statements PRIMARY KEY (lmscmi5statementid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscmi5statements_registration_statement ON public.lmscmi5statements (lmsruntimeregistrationid, statementid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscmi5statements_department ON public.lmscmi5statements (departmentid, lmsruntimeregistrationid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscmi5statedocuments (
	lmscmi5statedocumentid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsruntimeregistrationid integer NOT NULL,
	activityid varchar(500) NOT NULL,
	stateid varchar(250) NOT NULL,
	contenttype text NULL,
	content text NULL,
	updatedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmscmi5statedocuments PRIMARY KEY (lmscmi5statedocumentid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscmi5statedocuments_key ON public.lmscmi5statedocuments (lmsruntimeregistrationid, stateid, activityid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscmi5statedocuments_department ON public.lmscmi5statedocuments (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmscmi5statedocuments;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscmi5statements;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsruntimesessions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsruntimeregistrations;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscontentpackages;");
		}
	}
}
