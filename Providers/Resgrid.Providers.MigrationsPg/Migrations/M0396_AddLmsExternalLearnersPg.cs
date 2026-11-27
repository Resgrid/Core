using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0396_AddLmsExternalLearners: lmslearnerportals, lmsexternallearners (e-mail and learner ids citext) and
	/// lmsexternallearnertokens, with the same indexes in lower case. Guarded for safe retry.
	/// </summary>
	[Migration(396)]
	public class M0396_AddLmsExternalLearnersPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslearnerportals (
	lmslearnerportalid serial NOT NULL,
	departmentid integer NOT NULL,
	portalkey citext NOT NULL,
	isenabled boolean NOT NULL DEFAULT false,
	selfregistration boolean NOT NULL DEFAULT false,
	publiclisting boolean NOT NULL DEFAULT false,
	accentcolor citext NULL,
	welcometext citext NULL,
	hidelogo boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmslearnerportals PRIMARY KEY (lmslearnerportalid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmslearnerportals_departmentid ON public.lmslearnerportals (departmentid);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmslearnerportals_portalkey ON public.lmslearnerportals (portalkey);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsexternallearners (
	lmsexternallearnerid serial NOT NULL,
	departmentid integer NOT NULL,
	learneruserid citext NOT NULL,
	email citext NULL,
	firstname citext NULL,
	lastname citext NULL,
	organization citext NULL,
	sponsorcontactid citext NULL,
	billingcontactid citext NULL,
	status integer NOT NULL DEFAULT 0,
	source integer NOT NULL DEFAULT 0,
	passwordhash text NULL,
	securitystamp text NOT NULL,
	emailconfirmedon timestamp without time zone NULL,
	accessfailedcount integer NOT NULL DEFAULT 0,
	lockoutendson timestamp without time zone NULL,
	lastsigninon timestamp without time zone NULL,
	invitedbyuserid citext NULL,
	createdon timestamp without time zone NOT NULL,
	updatedon timestamp without time zone NULL,
	erasedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsexternallearners PRIMARY KEY (lmsexternallearnerid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsexternallearners_learneruserid ON public.lmsexternallearners (learneruserid);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsexternallearners_email ON public.lmsexternallearners (departmentid, email) WHERE email IS NOT NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsexternallearners_department_status ON public.lmsexternallearners (departmentid, status);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsexternallearnertokens (
	lmsexternallearnertokenid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsexternallearnerid integer NOT NULL,
	purpose integer NOT NULL DEFAULT 0,
	tokenhash text NOT NULL,
	lmsclasssessionid integer NULL,
	expireson timestamp without time zone NOT NULL,
	usedon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsexternallearnertokens PRIMARY KEY (lmsexternallearnertokenid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsexternallearnertokens_tokenhash ON public.lmsexternallearnertokens (tokenhash);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsexternallearnertokens_learner ON public.lmsexternallearnertokens (departmentid, lmsexternallearnerid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsexternallearnertokens;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsexternallearners;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmslearnerportals;");
		}
	}
}
