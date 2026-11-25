using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0405_AddLmsClassCostsAndResources: the same tables and indexes in lower case. Guarded for safe retry.</summary>
	[Migration(405)]
	public class M0405_AddLmsClassCostsAndResourcesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclasscostlines (
	lmsclasscostlineid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	description citext NULL,
	amount numeric(14,2) NOT NULL DEFAULT 0,
	currency citext NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmsclasscostlines PRIMARY KEY (lmsclasscostlineid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclasscostlines_session ON public.lmsclasscostlines (departmentid, lmsclasssessionid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmstrainingresources (
	lmstrainingresourceid serial NOT NULL,
	departmentid integer NOT NULL,
	name citext NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	poiid integer NULL,
	capacity integer NULL,
	notes citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmstrainingresources PRIMARY KEY (lmstrainingresourceid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmstrainingresources_departmentid ON public.lmstrainingresources (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclassresourcebookings (
	lmsclassresourcebookingid serial NOT NULL,
	departmentid integer NOT NULL,
	lmstrainingresourceid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	startson timestamp without time zone NOT NULL,
	endson timestamp without time zone NOT NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmsclassresourcebookings PRIMARY KEY (lmsclassresourcebookingid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassresourcebookings_resource ON public.lmsclassresourcebookings (departmentid, lmstrainingresourceid, startson);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassresourcebookings_session ON public.lmsclassresourcebookings (departmentid, lmsclasssessionid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclassresourcebookings;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmstrainingresources;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclasscostlines;");
		}
	}
}
