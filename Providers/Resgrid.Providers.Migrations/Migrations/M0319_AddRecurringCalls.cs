using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Recurring calls: a call that repeats on a schedule ("every Tuesday and Thursday at 08:00, 13:00 and 19:00"). RecurringCalls
	/// holds the schedule and the call it creates; each occurrence becomes an ordinary scheduled call ahead of its dispatch time.
	/// RecurringCallOccurrences records the occurrences that were created (or are being created) and those skipped; its unique
	/// (RecurringCallId, OccurrenceOn) index is the claim that stops one occurrence being created twice. Calls.RecurringCallId links a
	/// created call back to its schedule. No foreign keys: recipients are id lists, a schedule is soft deleted, and
	/// UnlinkedDepartmentCleanup removes both tables. IX_Calls_DispatchOn serves the Scheduled Calls calendar (only calls with a
	/// dispatch time). Existence-guarded for safe retry.
	/// </summary>
	[Migration(319)]
	public class M0319_AddRecurringCalls : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RecurringCalls").Exists())
			{
				Create.Table("RecurringCalls")
					.WithColumn("RecurringCallId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(500).NotNullable()
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("Frequency").AsInt32().NotNullable()
					.WithColumn("RepeatInterval").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("DaysOfWeek").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MonthlyDay").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MonthlyWeek").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MonthlyWeekday").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Times").AsString(500).NotNullable()
					.WithColumn("StartsOn").AsDateTime2().NotNullable()
					.WithColumn("EndsOn").AsDateTime2().Nullable()
					.WithColumn("LeadTimeMinutes").AsInt32().NotNullable()
					.WithColumn("NatureOfCall").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("Address").AsString(int.MaxValue).Nullable()
					.WithColumn("GeoLocationData").AsString(int.MaxValue).Nullable()
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Type").AsString(int.MaxValue).Nullable()
					.WithColumn("ContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContactNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("ExternalIdentifier").AsString(int.MaxValue).Nullable()
					.WithColumn("ReferenceNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("IncidentNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("MapPage").AsString(int.MaxValue).Nullable()
					.WithColumn("DestinationPoiId").AsInt32().Nullable()
					.WithColumn("CheckInTimersEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DispatchUserIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DispatchGroupIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DispatchUnitIds").AsString(int.MaxValue).Nullable()
					.WithColumn("DispatchRoleIds").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();

				Create.Index("IX_RecurringCalls_Department").OnTable("RecurringCalls").OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("RecurringCallOccurrences").Exists())
			{
				Create.Table("RecurringCallOccurrences")
					.WithColumn("RecurringCallOccurrenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("RecurringCallId").AsInt32().NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("OccurrenceOn").AsDateTime2().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();

				Create.Index("UX_RecurringCallOccurrences_Occurrence").OnTable("RecurringCallOccurrences")
					.OnColumn("RecurringCallId").Ascending()
					.OnColumn("OccurrenceOn").Ascending()
					.WithOptions().Unique();

				Create.Index("IX_RecurringCallOccurrences_Department").OnTable("RecurringCallOccurrences")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("OccurrenceOn").Ascending();
			}

			Execute.Sql("IF COL_LENGTH('Calls', 'RecurringCallId') IS NULL ALTER TABLE [Calls] ADD [RecurringCallId] int NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Calls_RecurringCallId' AND object_id = OBJECT_ID('Calls')) " +
						"CREATE INDEX [IX_Calls_RecurringCallId] ON [Calls] ([RecurringCallId], [DispatchOn]) WHERE [RecurringCallId] IS NOT NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Calls_DispatchOn' AND object_id = OBJECT_ID('Calls')) " +
						"CREATE INDEX [IX_Calls_DispatchOn] ON [Calls] ([DepartmentId], [DispatchOn]) WHERE [DispatchOn] IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Calls_DispatchOn' AND object_id = OBJECT_ID('Calls')) DROP INDEX [IX_Calls_DispatchOn] ON [Calls];");
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Calls_RecurringCallId' AND object_id = OBJECT_ID('Calls')) DROP INDEX [IX_Calls_RecurringCallId] ON [Calls];");
			Execute.Sql("IF COL_LENGTH('Calls', 'RecurringCallId') IS NOT NULL ALTER TABLE [Calls] DROP COLUMN [RecurringCallId];");

			if (Schema.Table("RecurringCallOccurrences").Exists())
				Delete.Table("RecurringCallOccurrences");

			if (Schema.Table("RecurringCalls").Exists())
				Delete.Table("RecurringCalls");
		}
	}
}
