using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// The initial schema created notes and documents with a misspelled "catery" column while the
	/// entities (and the Dapper-generated INSERT/UPDATE statements) use "category" — every save
	/// against a database built from M0001 failed with 42703 "column category does not exist".
	/// Renames the column where the typo exists; guarded so databases that already have the
	/// correct column (or were hand-fixed) are untouched.
	/// </summary>
	[Migration(113)]
	public class M0113_FixNotesDocumentsCategoryColumnPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'notes' AND column_name = 'catery')
		AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'notes' AND column_name = 'category') THEN
		ALTER TABLE public.notes RENAME COLUMN catery TO category;
	END IF;

	IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'documents' AND column_name = 'catery')
		AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'documents' AND column_name = 'category') THEN
		ALTER TABLE public.documents RENAME COLUMN catery TO category;
	END IF;
END $$;");
		}

		public override void Down()
		{
			// One-way typo fix; nothing to restore.
		}
	}
}
