using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0395_AddLmsDataExports: the same tables and indexes in lower case; the requesting member id is citext, the object
	/// key and hash stay exact. Guarded for safe retry.
	/// </summary>
	[Migration(395)]
	public class M0395_AddLmsDataExportsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsdataexports (
	lmsdataexportid serial NOT NULL,
	departmentid integer NOT NULL,
	scope integer NOT NULL DEFAULT 0,
	includemedia boolean NOT NULL DEFAULT false,
	status integer NOT NULL DEFAULT 0,
	requestedbyuserid citext NOT NULL,
	requestedon timestamp without time zone NOT NULL,
	claimedon timestamp without time zone NULL,
	attempts integer NOT NULL DEFAULT 0,
	completedon timestamp without time zone NULL,
	expireson timestamp without time zone NULL,
	objectkey character varying(500) NULL,
	storedindatabase boolean NOT NULL DEFAULT false,
	filename citext NULL,
	sizebytes bigint NOT NULL DEFAULT 0,
	sha256 character varying(64) NULL,
	countsjson text NULL,
	error citext NULL,
	downloadcount integer NOT NULL DEFAULT 0,
	lastdownloadedon timestamp without time zone NULL,
	purgedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsdataexports PRIMARY KEY (lmsdataexportid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsdataexports_department ON public.lmsdataexports (departmentid, requestedon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsdataexports_queue ON public.lmsdataexports (status, requestedon) WHERE status IN (0, 1);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsdataexports_expiry ON public.lmsdataexports (expireson) WHERE status = 2 AND purgedon IS NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsdataexportblobs (
	lmsdataexportblobid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsdataexportid integer NOT NULL,
	data bytea NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsdataexportblobs PRIMARY KEY (lmsdataexportblobid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsdataexportblobs_export ON public.lmsdataexportblobs (departmentid, lmsdataexportid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsdataexportblobs;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsdataexports;");
		}
	}
}
