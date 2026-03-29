using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(60)]
	public class M0060_FixCheckInTimerNullUniquenessPg : Migration
	{
		public override void Up()
		{
			// Drop existing unique indexes from M0058
			Execute.Sql("DROP INDEX IF EXISTS uq_checkintimerconfigs_dept_target_unit;");
			Execute.Sql("DROP INDEX IF EXISTS uq_checkintimeroverrides_dept_call_target_unit;");

			// Remove duplicate rows that may exist from before the NULLS NOT DISTINCT
			// constraint was applied (keep the most recently modified row per group)
			Execute.Sql(@"
				DELETE FROM checkintimerconfigs
				WHERE checkintimerconfigid IN (
					SELECT checkintimerconfigid FROM (
						SELECT checkintimerconfigid,
							ROW_NUMBER() OVER (
								PARTITION BY departmentid, timertargettype, COALESCE(unittypeid, -1)
								ORDER BY COALESCE(updatedon, createdon) DESC, checkintimerconfigid DESC
							) AS rn
						FROM checkintimerconfigs
					) sub WHERE rn > 1
				);
			");

			Execute.Sql(@"
				DELETE FROM checkintimeroverrides
				WHERE checkintimeroverrideid IN (
					SELECT checkintimeroverrideid FROM (
						SELECT checkintimeroverrideid,
							ROW_NUMBER() OVER (
								PARTITION BY departmentid, COALESCE(calltypeid, -1), COALESCE(callpriority, -1), timertargettype, COALESCE(unittypeid, -1)
								ORDER BY COALESCE(updatedon, createdon) DESC, checkintimeroverrideid DESC
							) AS rn
						FROM checkintimeroverrides
					) sub WHERE rn > 1
				);
			");

			// Recreate NULLS NOT DISTINCT unique indexes
			Execute.Sql(@"
				CREATE UNIQUE INDEX uq_checkintimerconfigs_dept_target_unit
				ON checkintimerconfigs (departmentid, timertargettype, unittypeid)
				NULLS NOT DISTINCT;
			");

			Execute.Sql(@"
				CREATE UNIQUE INDEX uq_checkintimeroverrides_dept_call_target_unit
				ON checkintimeroverrides (departmentid, calltypeid, callpriority, timertargettype, unittypeid)
				NULLS NOT DISTINCT;
			");
		}

		public override void Down()
		{
			// No-op: indexes already exist from M0058 and are just being rebuilt here
		}
	}
}
