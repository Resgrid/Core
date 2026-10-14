using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// List base types and Calls.CallTypeId (see the SQL Server M0272). The name match is case-insensitive like SQL Server's
	/// default collation; backfilled a CallId range at a time.
	/// </summary>
	// No wrapping transaction: the backfill commits a CallId range at a time, and every step is safe to re-run.
	[Migration(272, TransactionBehavior.None)]
	public class M0272_AddListBaseTypesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE departmentcallpriorities ADD COLUMN IF NOT EXISTS basetype integer NOT NULL DEFAULT -1;");
			Execute.Sql("ALTER TABLE calltypes ADD COLUMN IF NOT EXISTS basetype integer NOT NULL DEFAULT -1;");
			Execute.Sql("ALTER TABLE unittypes ADD COLUMN IF NOT EXISTS basetype integer NOT NULL DEFAULT -1;");
			Execute.Sql("ALTER TABLE calls ADD COLUMN IF NOT EXISTS calltypeid integer NULL;");

			Execute.Sql(@"
DO $$
DECLARE
	lo integer := 0;
	max_id integer;
BEGIN
	SELECT COALESCE(MAX(callid), 0) INTO max_id FROM calls;

	WHILE lo < max_id LOOP
		UPDATE calls c SET calltypeid = m.calltypeid
		FROM (SELECT departmentid, lower(type::text) AS typename, MIN(calltypeid) AS calltypeid FROM calltypes GROUP BY departmentid, lower(type::text)) m
		WHERE m.departmentid = c.departmentid AND m.typename = lower(c.type::text)
			AND c.callid > lo AND c.callid <= lo + 50000 AND c.calltypeid IS NULL;

		COMMIT;
		lo := lo + 50000;
	END LOOP;
END $$;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE calls DROP COLUMN IF EXISTS calltypeid;");
			Execute.Sql("ALTER TABLE unittypes DROP COLUMN IF EXISTS basetype;");
			Execute.Sql("ALTER TABLE calltypes DROP COLUMN IF EXISTS basetype;");
			Execute.Sql("ALTER TABLE departmentcallpriorities DROP COLUMN IF EXISTS basetype;");
		}
	}
}
