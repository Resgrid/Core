using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0390_AddLmsReportsAndCertificationCycles: the same tables and indexes in lower case, text columns citext. Guarded for safe retry.
	/// </summary>
	[Migration(390)]
	public class M0390_AddLmsReportsAndCertificationCyclesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsreportfolders (
	lmsreportfolderid serial NOT NULL,
	departmentid integer NOT NULL,
	name citext NOT NULL,
	sortorder integer NOT NULL DEFAULT 0,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmsreportfolders PRIMARY KEY (lmsreportfolderid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsreportfolders_departmentid ON public.lmsreportfolders (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsreportdefinitions (
	lmsreportdefinitionid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsreportfolderid integer NULL,
	reportkey citext NOT NULL,
	name citext NOT NULL,
	parametersjson citext NULL,
	format integer NOT NULL DEFAULT 0,
	frequency integer NOT NULL DEFAULT 0,
	dayofweek integer NOT NULL DEFAULT 0,
	dayofmonth integer NOT NULL DEFAULT 1,
	hourlocal integer NOT NULL DEFAULT 6,
	recipientuserids citext NULL,
	nextrunutc timestamp without time zone NULL,
	lastrunutc timestamp without time zone NULL,
	lastrunstatus integer NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmsreportdefinitions PRIMARY KEY (lmsreportdefinitionid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsreportdefinitions_departmentid ON public.lmsreportdefinitions (departmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsreportdefinitions_due ON public.lmsreportdefinitions (nextrunutc) WHERE isdeleted = false AND frequency > 0 AND nextrunutc IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsreportruns (
	lmsreportrunid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsreportdefinitionid integer NOT NULL,
	ranon timestamp without time zone NOT NULL,
	status integer NOT NULL DEFAULT 0,
	recipients integer NOT NULL DEFAULT 0,
	rowtotal integer NOT NULL DEFAULT 0,
	error citext NULL,
	CONSTRAINT pk_lmsreportruns PRIMARY KEY (lmsreportrunid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsreportruns_definition ON public.lmsreportruns (lmsreportdefinitionid, ranon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsreportruns_departmentid ON public.lmsreportruns (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscertificationcyclerules (
	lmscertificationcycleruleid serial NOT NULL,
	departmentid integer NOT NULL,
	departmentcertificationtypeid integer NOT NULL,
	autorenewmode integer NOT NULL DEFAULT 0,
	alertoncompletion boolean NOT NULL DEFAULT false,
	alertonrenewal boolean NOT NULL DEFAULT false,
	alertleaddayscsv citext NULL,
	alertonexpired boolean NOT NULL DEFAULT false,
	notifysupervisors boolean NOT NULL DEFAULT false,
	notifyadmins boolean NOT NULL DEFAULT false,
	namedrecipientuserids citext NULL,
	alertsfrom timestamp without time zone NOT NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmscertificationcyclerules PRIMARY KEY (lmscertificationcycleruleid)
);");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscertificationcyclerules_type ON public.lmscertificationcyclerules (departmentid, departmentcertificationtypeid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscertificationalerts (
	lmscertificationalertid serial NOT NULL,
	departmentid integer NOT NULL,
	personnelcertificationid integer NOT NULL,
	kind integer NOT NULL,
	alertkey citext NOT NULL,
	senton timestamp without time zone NOT NULL,
	recipients integer NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmscertificationalerts PRIMARY KEY (lmscertificationalertid)
);");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscertificationalerts_record_kind_key ON public.lmscertificationalerts (personnelcertificationid, kind, alertkey);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscertificationalerts_departmentid ON public.lmscertificationalerts (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsprogramstates (
	departmentid integer NOT NULL,
	cyclesweeplocaldate timestamp without time zone NULL,
	familiarizationscannedthrough timestamp without time zone NULL,
	updatedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsprogramstates PRIMARY KEY (departmentid)
);");

		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsprogramstates;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscertificationalerts;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscertificationcyclerules;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsreportruns;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsreportdefinitions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsreportfolders;");
		}
	}
}
