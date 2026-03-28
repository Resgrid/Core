using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(60)]
	public class M0060_FixCheckInTimerNullUniqueness : Migration
	{
		public override void Up()
		{
			// Drop the filtered unique indexes from M0058 that allow multiple NULL rows
			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerConfigs_Dept_Target_Unit ON CheckInTimerConfigs;");
			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerOverrides_Dept_Call_Target_Unit ON CheckInTimerOverrides;");

			// Remove duplicate rows that may have accumulated while the filtered indexes
			// allowed multiple NULL entries (keep the row with the latest CheckInTimerConfigId per group)
			Execute.Sql(@"
				WITH cte AS (
					SELECT CheckInTimerConfigId,
						ROW_NUMBER() OVER (
							PARTITION BY DepartmentId, TimerTargetType, ISNULL(UnitTypeId, -1)
							ORDER BY CheckInTimerConfigId DESC
						) AS rn
					FROM CheckInTimerConfigs
				)
				DELETE FROM cte WHERE rn > 1;
			");

			Execute.Sql(@"
				WITH cte AS (
					SELECT CheckInTimerOverrideId,
						ROW_NUMBER() OVER (
							PARTITION BY DepartmentId, ISNULL(CallTypeId, -1), ISNULL(CallPriority, -1), TimerTargetType, ISNULL(UnitTypeId, -1)
							ORDER BY CheckInTimerOverrideId DESC
						) AS rn
					FROM CheckInTimerOverrides
				)
				DELETE FROM cte WHERE rn > 1;
			");

			// Add persisted computed columns that coalesce NULLs to sentinel values
			// so SQL Server treats NULL as a distinct value for uniqueness (matching PostgreSQL NULLS NOT DISTINCT)
			Execute.Sql(@"
				ALTER TABLE CheckInTimerConfigs
				ADD UnitTypeId_Unique AS ISNULL(UnitTypeId, -1) PERSISTED;
			");

			Execute.Sql(@"
				ALTER TABLE CheckInTimerOverrides
				ADD CallTypeId_Unique AS ISNULL(CallTypeId, -1) PERSISTED,
					CallPriority_Unique AS ISNULL(CallPriority, -1) PERSISTED,
					UnitTypeId_Unique AS ISNULL(UnitTypeId, -1) PERSISTED;
			");

			// Create unique indexes on the computed columns
			Execute.Sql(@"
				CREATE UNIQUE NONCLUSTERED INDEX UQ_CheckInTimerConfigs_Dept_Target_Unit
				ON CheckInTimerConfigs (DepartmentId, TimerTargetType, UnitTypeId_Unique);
			");

			Execute.Sql(@"
				CREATE UNIQUE NONCLUSTERED INDEX UQ_CheckInTimerOverrides_Dept_Call_Target_Unit
				ON CheckInTimerOverrides (DepartmentId, CallTypeId_Unique, CallPriority_Unique, TimerTargetType, UnitTypeId_Unique);
			");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerConfigs_Dept_Target_Unit ON CheckInTimerConfigs;");
			Execute.Sql("DROP INDEX IF EXISTS UQ_CheckInTimerOverrides_Dept_Call_Target_Unit ON CheckInTimerOverrides;");

			Execute.Sql("ALTER TABLE CheckInTimerOverrides DROP COLUMN CallTypeId_Unique, CallPriority_Unique, UnitTypeId_Unique;");
			Execute.Sql("ALTER TABLE CheckInTimerConfigs DROP COLUMN UnitTypeId_Unique;");

			// Restore the filtered indexes from M0058
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
		}
	}
}
