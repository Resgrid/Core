using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(58)]
	public class M0058_FixCheckInTimerNullableUniqueConstraintsPg : Migration
	{
		public override void Up()
		{
			// Drop the existing unique constraints that allow duplicate NULL rows
			Delete.UniqueConstraint("uq_checkintimerconfigs_dept_target_unit")
				.FromTable("checkintimerconfigs");

			Delete.UniqueConstraint("uq_checkintimeroverrides_dept_call_target_unit")
				.FromTable("checkintimeroverrides");

			// Replace with NULLS NOT DISTINCT unique indexes (PostgreSQL 15+)
			// so that NULL values are treated as equal for uniqueness checks,
			// preventing duplicate "Any/None" rules.
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

			// Add compound indexes for "latest check-in" lookups by user and unit
			Create.Index("ix_checkinrecords_callid_userid_timestamp")
				.OnTable("checkinrecords")
				.OnColumn("callid").Ascending()
				.OnColumn("userid").Ascending()
				.OnColumn("timestamp").Descending();

			Create.Index("ix_checkinrecords_callid_unitid_timestamp")
				.OnTable("checkinrecords")
				.OnColumn("callid").Ascending()
				.OnColumn("unitid").Ascending()
				.OnColumn("timestamp").Descending();

			// Add composite indexes for CalendarItemCheckIns query patterns
			Create.Index("ix_calendaritemcheckins_calendaritemid_checkintime")
				.OnTable("calendaritemcheckins")
				.OnColumn("calendaritemid").Ascending()
				.OnColumn("checkintime").Descending();

			Create.Index("ix_calendaritemcheckins_departmentid_checkintime")
				.OnTable("calendaritemcheckins")
				.OnColumn("departmentid").Ascending()
				.OnColumn("checkintime").Descending();

			Create.Index("ix_calendaritemcheckins_departmentid_userid_checkintime")
				.OnTable("calendaritemcheckins")
				.OnColumn("departmentid").Ascending()
				.OnColumn("userid").Ascending()
				.OnColumn("checkintime").Descending();
		}

		public override void Down()
		{
			Delete.Index("ix_calendaritemcheckins_departmentid_userid_checkintime").OnTable("calendaritemcheckins");
			Delete.Index("ix_calendaritemcheckins_departmentid_checkintime").OnTable("calendaritemcheckins");
			Delete.Index("ix_calendaritemcheckins_calendaritemid_checkintime").OnTable("calendaritemcheckins");

			Delete.Index("ix_checkinrecords_callid_unitid_timestamp").OnTable("checkinrecords");
			Delete.Index("ix_checkinrecords_callid_userid_timestamp").OnTable("checkinrecords");

			Execute.Sql("DROP INDEX IF EXISTS uq_checkintimerconfigs_dept_target_unit;");
			Execute.Sql("DROP INDEX IF EXISTS uq_checkintimeroverrides_dept_call_target_unit;");

			Create.UniqueConstraint("uq_checkintimerconfigs_dept_target_unit")
				.OnTable("checkintimerconfigs")
				.Columns("departmentid", "timertargettype", "unittypeid");

			Create.UniqueConstraint("uq_checkintimeroverrides_dept_call_target_unit")
				.OnTable("checkintimeroverrides")
				.Columns("departmentid", "calltypeid", "callpriority", "timertargettype", "unittypeid");
		}
	}
}
