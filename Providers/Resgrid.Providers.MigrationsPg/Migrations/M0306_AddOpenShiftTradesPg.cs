using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Open shift trade requests and static shift (workshift) day trades; see the SQL Server M0306_AddOpenShiftTrades.
	/// </summary>
	[Migration(306)]
	public class M0306_AddOpenShiftTradesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE shiftsignuptrades ALTER COLUMN sourceshiftsignupid DROP NOT NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS departmentid integer NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS sourceuserid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS sourceday timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS isopen boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS requestedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS workshiftid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS workshiftdayid citext NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS unitid integer NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ADD COLUMN IF NOT EXISTS departmentgroupid integer NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptradeusers ADD COLUMN IF NOT EXISTS respondedon timestamp without time zone NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_shiftsignuptrades_departmentid_sourceday ON shiftsignuptrades (departmentid, sourceday);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_shiftsignuptrades_departmentid_sourceday;");
			Execute.Sql("ALTER TABLE shiftsignuptradeusers DROP COLUMN IF EXISTS respondedon;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS departmentgroupid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS unitid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS workshiftdayid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS workshiftid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS requestedon;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS isopen;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS sourceday;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS sourceuserid;");
			Execute.Sql("ALTER TABLE shiftsignuptrades DROP COLUMN IF EXISTS departmentid;");
			Execute.Sql("DELETE FROM shiftsignuptrades WHERE sourceshiftsignupid IS NULL;");
			Execute.Sql("ALTER TABLE shiftsignuptrades ALTER COLUMN sourceshiftsignupid SET NOT NULL;");
		}
	}
}
