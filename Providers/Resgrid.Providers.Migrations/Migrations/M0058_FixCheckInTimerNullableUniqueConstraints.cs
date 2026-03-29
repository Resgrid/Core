using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(58)]
	public class M0058_FixCheckInTimerNullableUniqueConstraints : Migration
	{
		public override void Up()
		{
			// Drop the existing unique constraints that prevent multiple NULLs
			Delete.UniqueConstraint("UQ_CheckInTimerConfigs_Dept_Target_Unit")
				.FromTable("CheckInTimerConfigs");

			Delete.UniqueConstraint("UQ_CheckInTimerOverrides_Dept_Call_Target_Unit")
				.FromTable("CheckInTimerOverrides");

			// Replace with filtered unique indexes that only enforce uniqueness
			// when nullable columns are NOT NULL, allowing multiple NULL rows.
			Execute.Sql(@"
				CREATE UNIQUE NONCLUSTERED INDEX UQ_CheckInTimerConfigs_Dept_Target_Unit
				ON CheckInTimerConfigs (DepartmentId, TimerTargetType, UnitTypeId)
				WHERE UnitTypeId IS NOT NULL;
			");

			Execute.Sql(@"
				CREATE UNIQUE NONCLUSTERED INDEX UQ_CheckInTimerOverrides_Dept_Call_Target_Unit
				ON CheckInTimerOverrides (DepartmentId, CallTypeId, CallPriority, TimerTargetType, UnitTypeId)
				WHERE CallTypeId IS NOT NULL AND CallPriority IS NOT NULL AND UnitTypeId IS NOT NULL;
			");

			// Add compound indexes for "latest check-in" lookups by user and unit
			Create.Index("IX_CheckInRecords_CallId_UserId_Timestamp")
				.OnTable("CheckInRecords")
				.OnColumn("CallId").Ascending()
				.OnColumn("UserId").Ascending()
				.OnColumn("Timestamp").Descending();

			Create.Index("IX_CheckInRecords_CallId_UnitId_Timestamp")
				.OnTable("CheckInRecords")
				.OnColumn("CallId").Ascending()
				.OnColumn("UnitId").Ascending()
				.OnColumn("Timestamp").Descending();

			// Add composite indexes for CalendarItemCheckIns query patterns
			Create.Index("IX_CalendarItemCheckIns_CalendarItemId_CheckInTime")
				.OnTable("CalendarItemCheckIns")
				.OnColumn("CalendarItemId").Ascending()
				.OnColumn("CheckInTime").Descending();

			Create.Index("IX_CalendarItemCheckIns_DepartmentId_CheckInTime")
				.OnTable("CalendarItemCheckIns")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("CheckInTime").Descending();

			Create.Index("IX_CalendarItemCheckIns_DepartmentId_UserId_CheckInTime")
				.OnTable("CalendarItemCheckIns")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("UserId").Ascending()
				.OnColumn("CheckInTime").Descending();
		}

		public override void Down()
		{
			Delete.Index("IX_CalendarItemCheckIns_DepartmentId_UserId_CheckInTime").OnTable("CalendarItemCheckIns");
			Delete.Index("IX_CalendarItemCheckIns_DepartmentId_CheckInTime").OnTable("CalendarItemCheckIns");
			Delete.Index("IX_CalendarItemCheckIns_CalendarItemId_CheckInTime").OnTable("CalendarItemCheckIns");

			Delete.Index("IX_CheckInRecords_CallId_UnitId_Timestamp").OnTable("CheckInRecords");
			Delete.Index("IX_CheckInRecords_CallId_UserId_Timestamp").OnTable("CheckInRecords");

			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerConfigs_Dept_Target_Unit ON CheckInTimerConfigs;");
			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerOverrides_Dept_Call_Target_Unit ON CheckInTimerOverrides;");

			Create.UniqueConstraint("UQ_CheckInTimerConfigs_Dept_Target_Unit")
				.OnTable("CheckInTimerConfigs")
				.Columns("DepartmentId", "TimerTargetType", "UnitTypeId");

			Create.UniqueConstraint("UQ_CheckInTimerOverrides_Dept_Call_Target_Unit")
				.OnTable("CheckInTimerOverrides")
				.Columns("DepartmentId", "CallTypeId", "CallPriority", "TimerTargetType", "UnitTypeId");
		}
	}
}
