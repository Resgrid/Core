using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0407_AddLmsSkillSheets: the same tables and indexes in lower case, member ids and text citext (hashes and the idempotency
	/// key stay exact). Guarded for safe retry.
	/// </summary>
	[Migration(407)]
	public class M0407_AddLmsSkillSheetsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillsheets (
	lmsskillsheetid serial NOT NULL,
	departmentid integer NOT NULL,
	code citext NULL,
	title citext NOT NULL,
	description citext NULL,
	kind integer NOT NULL DEFAULT 0,
	status integer NOT NULL DEFAULT 0,
	currentversionid integer NULL,
	categoryid integer NULL,
	evaluatoreligibilityjson text NULL,
	nextsignereligibilityjson text NULL,
	allowselfevaluation boolean NOT NULL DEFAULT false,
	allowcrewevaluation boolean NOT NULL DEFAULT false,
	templatekey citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsskillsheets PRIMARY KEY (lmsskillsheetid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillsheets_department ON public.lmsskillsheets (departmentid, isdeleted);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsskillsheetversions (
	lmsskillsheetversionid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsskillsheetid integer NOT NULL,
	major integer NOT NULL DEFAULT 0,
	minor integer NOT NULL DEFAULT 0,
	status integer NOT NULL DEFAULT 0,
	contentjson text NULL,
	contenthash varchar(64) NULL,
	changesummary citext NULL,
	passthreshold numeric(5,2) NOT NULL DEFAULT 100,
	maxattempts integer NOT NULL DEFAULT 0,
	attemptwindowdays integer NOT NULL DEFAULT 0,
	timelimitseconds integer NOT NULL DEFAULT 0,
	defaultminutes integer NOT NULL DEFAULT 0,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	publishedon timestamp without time zone NULL,
	publishedbyuserid citext NULL,
	supersededon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsskillsheetversions PRIMARY KEY (lmsskillsheetversionid)
);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsskillsheetversions_sheet ON public.lmsskillsheetversions (departmentid, lmsskillsheetid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillsheetversions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsskillsheets;");
		}
	}
}
