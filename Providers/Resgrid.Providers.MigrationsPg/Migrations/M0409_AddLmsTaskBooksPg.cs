using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0409_AddLmsTaskBooks: the same tables and indexes in lower case, member ids and text citext (hashes and the idempotency
	/// key stay exact). Guarded for safe retry.
	/// </summary>
	[Migration(409)]
	public class M0409_AddLmsTaskBooksPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstaskbookdefinitions (
	lmstaskbookdefinitionid serial NOT NULL,
	departmentid integer NOT NULL,
	positioncode citext NOT NULL,
	name citext NOT NULL,
	description citext NULL,
	family integer NOT NULL DEFAULT 0,
	tasksjson text NULL,
	certificationtypeid integer NULL,
	validitymonths integer NOT NULL DEFAULT 36,
	recertifymonths integer NOT NULL DEFAULT 0,
	evaluatoreligibilityjson text NULL,
	finalevaluatoreligibilityjson text NULL,
	certifyingeligibilityjson text NULL,
	templatekey citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmstaskbookdefinitions PRIMARY KEY (lmstaskbookdefinitionid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstaskbookdefinitions_department ON public.lmstaskbookdefinitions (departmentid, isdeleted);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstaskbooks (
	lmstaskbookid serial NOT NULL,
	departmentid integer NOT NULL,
	lmstaskbookdefinitionid integer NOT NULL,
	userid citext NOT NULL,
	positioncode citext NOT NULL,
	name citext NOT NULL,
	family integer NOT NULL DEFAULT 0,
	tasksjson text NULL,
	evaluatoreligibilityjson text NULL,
	finalevaluatoreligibilityjson text NULL,
	certifyingeligibilityjson text NULL,
	certificationtypeid integer NULL,
	personnelcertificationid integer NULL,
	recertifymonths integer NOT NULL DEFAULT 0,
	initiatedon timestamp without time zone NOT NULL,
	initiatedbyuserid citext NULL,
	expireson timestamp without time zone NOT NULL,
	status integer NOT NULL DEFAULT 0,
	signaturerequestid integer NULL,
	finalevaluatoruserid citext NULL,
	certifyingofficialuserid citext NULL,
	recommendedon timestamp without time zone NULL,
	certifiedon timestamp without time zone NULL,
	certifiedbyuserid citext NULL,
	closedon timestamp without time zone NULL,
	closereason citext NULL,
	updatedon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmstaskbooks PRIMARY KEY (lmstaskbookid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstaskbooks_department_user ON public.lmstaskbooks (departmentid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstaskbooks_status_expires ON public.lmstaskbooks (status, expireson);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmstaskbooks;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmstaskbookdefinitions;");
		}
	}
}
