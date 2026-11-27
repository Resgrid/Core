using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0397_AddLmsPublicClasses: lmspublicclasses, lmsclassfees and lmsexternalcourseaccess in lower case. Guarded for safe retry.</summary>
	[Migration(397)]
	public class M0397_AddLmsPublicClassesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmspublicclasses (
	lmspublicclassid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	ispublic boolean NOT NULL DEFAULT false,
	feeamount numeric(14,2) NULL,
	currency citext NULL,
	ratecarditemid citext NULL,
	createdon timestamp without time zone NOT NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmspublicclasses PRIMARY KEY (lmspublicclassid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmspublicclasses_session ON public.lmspublicclasses (departmentid, lmsclasssessionid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclassfees (
	lmsclassfeeid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	lmsclassenrollmentid integer NOT NULL,
	learneruserid citext NOT NULL,
	amount numeric(14,2) NOT NULL DEFAULT 0,
	currency citext NULL,
	waived boolean NOT NULL DEFAULT false,
	sponsorcontactid citext NULL,
	invoiceid text NULL,
	invoicedon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsclassfees PRIMARY KEY (lmsclassfeeid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsclassfees_enrollment ON public.lmsclassfees (departmentid, lmsclassenrollmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassfees_session ON public.lmsclassfees (departmentid, lmsclasssessionid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassfees_learner ON public.lmsclassfees (departmentid, learneruserid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsexternalcourseaccess (
	lmsexternalcourseaccessid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	access integer NOT NULL DEFAULT 0,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmsexternalcourseaccess PRIMARY KEY (lmsexternalcourseaccessid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsexternalcourseaccess_course ON public.lmsexternalcourseaccess (departmentid, lmscourseid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsexternalcourseaccess;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclassfees;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmspublicclasses;");
		}
	}
}
