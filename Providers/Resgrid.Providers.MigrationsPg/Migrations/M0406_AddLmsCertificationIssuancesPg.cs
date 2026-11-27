using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0406_AddLmsCertificationIssuances: lmscertificationissuances in lower case. User ids are citext; the basis is text.
	/// Guarded for safe retry.
	/// </summary>
	[Migration(406)]
	public class M0406_AddLmsCertificationIssuancesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscertificationissuances (
	lmscertificationissuanceid serial NOT NULL,
	departmentid integer NOT NULL,
	userid citext NOT NULL,
	personnelcertificationid integer NOT NULL,
	departmentcertificationtypeid integer NOT NULL,
	source integer NOT NULL,
	sourcerefid citext NULL,
	action integer NOT NULL,
	previousstatus integer NULL,
	previousexpireson timestamp without time zone NULL,
	previousrecievedon timestamp without time zone NULL,
	newstatus integer NOT NULL,
	newexpireson timestamp without time zone NULL,
	basisrecordids text NULL,
	completedon timestamp without time zone NOT NULL,
	issuedon timestamp without time zone NOT NULL,
	issuedbyuserid citext NULL,
	state integer NOT NULL DEFAULT 0,
	rolledbackon timestamp without time zone NULL,
	rolledbackbyuserid citext NULL,
	rollbackreason text NULL,
	CONSTRAINT pk_lmscertificationissuances PRIMARY KEY (lmscertificationissuanceid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscertificationissuances_user ON public.lmscertificationissuances (departmentid, userid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscertificationissuances_certification ON public.lmscertificationissuances (departmentid, personnelcertificationid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmscertificationissuances;");
		}
	}
}
