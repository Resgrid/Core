using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Supervisor approval and per-day roster edits for shifts (see the SQL Server M0234).
	/// </summary>
	[Migration(234)]
	public class M0234_AddShiftApprovalsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE shiftsignups ADD COLUMN IF NOT EXISTS approvalpending boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE shiftsignups ADD COLUMN IF NOT EXISTS assignedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignups ADD COLUMN IF NOT EXISTS reviewedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignups ADD COLUMN IF NOT EXISTS reviewedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE shiftsignups ADD COLUMN IF NOT EXISTS reviewnote citext NULL;");

			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS approvalpending boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS reviewedbyuserid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS reviewedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS reviewnote citext NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS reviewnote;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS reviewedon;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS reviewedbyuserid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS approvalpending;");

			Execute.Sql("ALTER TABLE shiftsignups DROP COLUMN IF EXISTS reviewnote;");
			Execute.Sql("ALTER TABLE shiftsignups DROP COLUMN IF EXISTS reviewedon;");
			Execute.Sql("ALTER TABLE shiftsignups DROP COLUMN IF EXISTS reviewedbyuserid;");
			Execute.Sql("ALTER TABLE shiftsignups DROP COLUMN IF EXISTS assignedbyuserid;");
			Execute.Sql("ALTER TABLE shiftsignups DROP COLUMN IF EXISTS approvalpending;");
		}
	}
}
