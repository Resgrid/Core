using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// The initial schema created departmentprofiles with misspelled "lo" and "ogleplus" columns (the same
	/// dropped-"go" damage M0113 repaired on notes and documents) while the entity, and so every
	/// Dapper-generated INSERT/UPDATE, uses "logo" and "googleplus" — every save against a database built
	/// from M0001 failed with 42703 "column googleplus of relation departmentprofiles does not exist", which
	/// broke the Department Profile page on first load when it tried to create the row. Renames each column
	/// where the typo exists; guarded so databases that already have the correct column (or were hand-fixed)
	/// are untouched.
	///
	/// No data moves: because every save failed, a database carrying the typo holds no departmentprofiles
	/// rows, so M0172's one-time legacy logo copy (which it skipped here, finding no logo column) has
	/// nothing to pick up.
	/// </summary>
	[Migration(231)]
	public class M0231_FixDepartmentProfilesLogoGooglePlusColumnsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'departmentprofiles' AND column_name = 'lo')
		AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'departmentprofiles' AND column_name = 'logo') THEN
		ALTER TABLE public.departmentprofiles RENAME COLUMN lo TO logo;
	END IF;

	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'departmentprofiles' AND column_name = 'ogleplus')
		AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'departmentprofiles' AND column_name = 'googleplus') THEN
		ALTER TABLE public.departmentprofiles RENAME COLUMN ogleplus TO googleplus;
	END IF;
END $$;");
		}

		public override void Down()
		{
			// One-way typo fix; nothing to restore.
		}
	}
}
