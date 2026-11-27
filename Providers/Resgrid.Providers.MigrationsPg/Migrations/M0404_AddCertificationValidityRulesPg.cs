using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0404_AddCertificationValidityRules: departmentcertificationtypes validity rule, year end, fixed date, grace and
	/// window, in lower case. Guarded for safe retry.
	/// </summary>
	[Migration(404)]
	public class M0404_AddCertificationValidityRulesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS validityrule integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS validityyearendmonth integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS validityyearendday integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS validityfixeddate timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS renewalgracedays integer NULL;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes ADD COLUMN IF NOT EXISTS renewalwindowmonths integer NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS renewalwindowmonths;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS renewalgracedays;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS validityfixeddate;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS validityyearendday;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS validityyearendmonth;");
			Execute.Sql("ALTER TABLE IF EXISTS public.departmentcertificationtypes DROP COLUMN IF EXISTS validityrule;");
		}
	}
}
