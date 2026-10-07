using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Unified Search call history backfill cursor (see the SQL Server M0261). Null on existing rows, so every activated
	/// department is backfilled once.
	/// </summary>
	[Migration(261)]
	public class M0261_AddSearchCallBackfillPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE searchindexstates ADD COLUMN IF NOT EXISTS callbackfillcursor integer NULL;");
			Execute.Sql("ALTER TABLE searchindexstates ADD COLUMN IF NOT EXISTS callbackfillcompletedon timestamp NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE searchindexstates DROP COLUMN IF EXISTS callbackfillcompletedon;");
			Execute.Sql("ALTER TABLE searchindexstates DROP COLUMN IF EXISTS callbackfillcursor;");
		}
	}
}
