using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0405_AddLmsCertificationRequirementSets: lmscertificationrequirementsets, lmscertificationrequirementitems and
	/// lmsprogramstates.requirementsetsweeplocaldate, in lower case. User ids are citext; the skill code is citext (matched case-insensitively).
	/// Guarded for safe retry.
	/// </summary>
	[Migration(405)]
	public class M0405_AddLmsCertificationRequirementSetsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscertificationrequirementsets (
	lmscertificationrequirementsetid serial NOT NULL,
	departmentid integer NOT NULL,
	departmentcertificationtypeid integer NOT NULL,
	isactive boolean NOT NULL DEFAULT true,
	issuetononholders boolean NOT NULL DEFAULT true,
	renewholders boolean NOT NULL DEFAULT true,
	duesoondays integer NOT NULL DEFAULT 30,
	notifymembers boolean NOT NULL DEFAULT true,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmscertificationrequirementsets PRIMARY KEY (lmscertificationrequirementsetid)
);");
			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscertificationrequirementsets_type ON public.lmscertificationrequirementsets (departmentid, departmentcertificationtypeid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscertificationrequirementitems (
	lmscertificationrequirementitemid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscertificationrequirementsetid integer NOT NULL,
	kind integer NOT NULL,
	targetrefid integer NULL,
	targetcode citext NULL,
	requiredhours numeric(9,2) NULL,
	requiredcount integer NULL,
	requirerecordedbyinstructor boolean NOT NULL DEFAULT false,
	label text NULL,
	sortorder integer NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmscertificationrequirementitems PRIMARY KEY (lmscertificationrequirementitemid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmscertificationrequirementitems_set ON public.lmscertificationrequirementitems (departmentid, lmscertificationrequirementsetid);");

			Execute.Sql("ALTER TABLE IF EXISTS public.lmsprogramstates ADD COLUMN IF NOT EXISTS requirementsetsweeplocaldate timestamp without time zone NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.lmsprogramstates DROP COLUMN IF EXISTS requirementsetsweeplocaldate;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscertificationrequirementitems;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscertificationrequirementsets;");
		}
	}
}
