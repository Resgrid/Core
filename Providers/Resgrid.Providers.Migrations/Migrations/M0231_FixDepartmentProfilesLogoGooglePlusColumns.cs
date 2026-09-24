using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The initial schema created DepartmentProfiles with misspelled "Lo" and "oglePlus" columns (the same
	/// dropped-"go" damage M0113 repaired on Notes and Documents) while the entity, and so every
	/// Dapper-generated INSERT/UPDATE, uses "Logo" and "GooglePlus" — no department profile could ever be
	/// saved against a database built from M0001, and the Department Profile page failed on first load when
	/// it tried to create the row. Renames each column where the typo exists; guarded so databases that
	/// already have the correct column (every database that predates M0001, or one hand-fixed) are untouched.
	///
	/// No data moves: because every save failed, a database carrying the typo holds no DepartmentProfiles
	/// rows, so M0172's one-time legacy logo copy (which it skipped here, finding no Logo column) has
	/// nothing to pick up.
	/// </summary>
	[Migration(231)]
	public class M0231_FixDepartmentProfilesLogoGooglePlusColumns : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF COL_LENGTH('dbo.DepartmentProfiles', 'Lo') IS NOT NULL AND COL_LENGTH('dbo.DepartmentProfiles', 'Logo') IS NULL
	EXEC sp_rename 'dbo.DepartmentProfiles.Lo', 'Logo', 'COLUMN';");

			Execute.Sql(@"
IF COL_LENGTH('dbo.DepartmentProfiles', 'oglePlus') IS NOT NULL AND COL_LENGTH('dbo.DepartmentProfiles', 'GooglePlus') IS NULL
	EXEC sp_rename 'dbo.DepartmentProfiles.oglePlus', 'GooglePlus', 'COLUMN';");
		}

		public override void Down()
		{
			// One-way typo fix; nothing to restore.
		}
	}
}
