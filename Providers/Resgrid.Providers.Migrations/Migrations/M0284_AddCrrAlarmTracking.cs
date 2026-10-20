using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Smoke / CO alarm tracking for community risk reduction (customer request: count the smoke, CO and combination
	/// alarms the department leaves each year, and keep the requests that lead to them).
	///  * RmsCrrActivities gains CoAlarmsInstalled and ComboAlarmsInstalled beside SmokeAlarmsInstalled (which now means
	///    smoke-only units), the AlarmReason (RmsAlarmReason: faulty, missing, expired ...), an optional CallId for a unit
	///    left during a call, and the RmsCrrAlarmRequestId the activity fulfilled. Existing rows keep their smoke count.
	///  * RmsCrrAlarmRequests is the request queue: Open, then Completed (an activity was recorded) or Declined. The
	///    resident is referenced by Contact id only; no name, phone or other identity is stored on the row.
	/// No foreign keys, like every Rms* table: the department purge discovers Rms* tables from the schema.
	/// </summary>
	[Migration(284)]
	public class M0284_AddCrrAlarmTracking : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'CoAlarmsInstalled') IS NULL ALTER TABLE [RmsCrrActivities] ADD [CoAlarmsInstalled] int NOT NULL CONSTRAINT [DF_RmsCrrActivities_CoAlarmsInstalled] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'ComboAlarmsInstalled') IS NULL ALTER TABLE [RmsCrrActivities] ADD [ComboAlarmsInstalled] int NOT NULL CONSTRAINT [DF_RmsCrrActivities_ComboAlarmsInstalled] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'AlarmReason') IS NULL ALTER TABLE [RmsCrrActivities] ADD [AlarmReason] int NOT NULL CONSTRAINT [DF_RmsCrrActivities_AlarmReason] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'CallId') IS NULL ALTER TABLE [RmsCrrActivities] ADD [CallId] int NULL;");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'RmsCrrAlarmRequestId') IS NULL ALTER TABLE [RmsCrrActivities] ADD [RmsCrrAlarmRequestId] nvarchar(36) NULL;");

			if (!Schema.Table("RmsCrrActivities").Index("IX_RmsCrrActivities_Department_Occupancy").Exists())
				Create.Index("IX_RmsCrrActivities_Department_Occupancy").OnTable("RmsCrrActivities").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();

			if (!Schema.Table("RmsCrrAlarmRequests").Exists())
			{
				Create.Table("RmsCrrAlarmRequests")
					.WithColumn("RmsCrrAlarmRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RequestNumber").AsString(50).NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("SmokeAlarmsRequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CoAlarmsRequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ComboAlarmsRequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RmsOccupancyId").AsString(36).Nullable()
					.WithColumn("LocationText").AsString(500).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("RequesterContactId").AsString(128).Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("ScheduledFor").AsDateTime2().Nullable()
					.WithColumn("AssignedToUserId").AsString(128).Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedByUserId").AsString(128).Nullable()
					.WithColumn("DeclineReason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResolutionNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("RmsCrrActivityId").AsString(36).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsCrrAlarmRequests_Department_Status").OnTable("RmsCrrAlarmRequests").OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending().OnColumn("RequestedOn").Ascending();
				Create.Index("IX_RmsCrrAlarmRequests_Department_Occupancy").OnTable("RmsCrrAlarmRequests").OnColumn("DepartmentId").Ascending().OnColumn("RmsOccupancyId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsCrrAlarmRequests").Exists())
				Delete.Table("RmsCrrAlarmRequests");

			if (Schema.Table("RmsCrrActivities").Index("IX_RmsCrrActivities_Department_Occupancy").Exists())
				Delete.Index("IX_RmsCrrActivities_Department_Occupancy").OnTable("RmsCrrActivities");

			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'RmsCrrAlarmRequestId') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP COLUMN [RmsCrrAlarmRequestId];");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'CallId') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP COLUMN [CallId];");
			Execute.Sql("IF OBJECT_ID('DF_RmsCrrActivities_AlarmReason', 'D') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP CONSTRAINT [DF_RmsCrrActivities_AlarmReason];");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'AlarmReason') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP COLUMN [AlarmReason];");
			Execute.Sql("IF OBJECT_ID('DF_RmsCrrActivities_ComboAlarmsInstalled', 'D') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP CONSTRAINT [DF_RmsCrrActivities_ComboAlarmsInstalled];");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'ComboAlarmsInstalled') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP COLUMN [ComboAlarmsInstalled];");
			Execute.Sql("IF OBJECT_ID('DF_RmsCrrActivities_CoAlarmsInstalled', 'D') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP CONSTRAINT [DF_RmsCrrActivities_CoAlarmsInstalled];");
			Execute.Sql("IF COL_LENGTH('RmsCrrActivities', 'CoAlarmsInstalled') IS NOT NULL ALTER TABLE [RmsCrrActivities] DROP COLUMN [CoAlarmsInstalled];");
		}
	}
}
