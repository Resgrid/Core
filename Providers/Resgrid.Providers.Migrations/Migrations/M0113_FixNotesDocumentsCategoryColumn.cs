using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The initial schema created Notes and Documents with a misspelled "Catery" column while the
	/// entities (and the Dapper-generated INSERT/UPDATE statements) use "Category" — every save
	/// against a database built from M0001 failed with "invalid column name 'Category'". Renames
	/// the column where the typo exists; guarded so databases that already have the correct
	/// column (or were hand-fixed) are untouched.
	/// </summary>
	[Migration(113)]
	public class M0113_FixNotesDocumentsCategoryColumn : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF COL_LENGTH('dbo.Notes', 'Catery') IS NOT NULL AND COL_LENGTH('dbo.Notes', 'Category') IS NULL
	EXEC sp_rename 'dbo.Notes.Catery', 'Category', 'COLUMN';");

			Execute.Sql(@"
IF COL_LENGTH('dbo.Documents', 'Catery') IS NOT NULL AND COL_LENGTH('dbo.Documents', 'Category') IS NULL
	EXEC sp_rename 'dbo.Documents.Catery', 'Category', 'COLUMN';");
		}

		public override void Down()
		{
			// One-way typo fix; nothing to restore.
		}
	}
}
