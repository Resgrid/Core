using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Recurring calls, second part: schedule metadata (description, the schedule's own contact, pickup facility and destination
	/// names and details), dispatcher mode (nothing created; the dispatcher is alerted to send each occurrence), the run card option,
	/// who to alert, and pause details (who, why, and an optional date it resumes on). RecurringCallAlerts holds what a dispatcher
	/// has to act on per occurrence (call not created, missed, nobody to dispatch, resources unavailable, dispatcher mode due); its
	/// unique (RecurringCallId, OccurrenceOn, Kind) index stops a worker pass raising or notifying twice, and the filtered index
	/// serves the open-alert list. No foreign keys; UnlinkedDepartmentCleanup removes the table. Existence-guarded for safe retry.
	/// </summary>
	[Migration(320)]
	public class M0320_ExtendRecurringCalls : Migration
	{
		public override void Up()
		{
			AddColumn("Description", "nvarchar(max) NULL");
			AddColumn("ScheduleContactName", "nvarchar(500) NULL");
			AddColumn("ScheduleContactNumber", "nvarchar(100) NULL");
			AddColumn("ScheduleContactEmail", "nvarchar(256) NULL");
			AddColumn("FacilityName", "nvarchar(500) NULL");
			AddColumn("FacilityDetails", "nvarchar(max) NULL");
			AddColumn("FacilityPoiId", "int NULL");
			AddColumn("DestinationName", "nvarchar(500) NULL");
			AddColumn("DestinationDetails", "nvarchar(max) NULL");
			AddColumn("CreationMode", "int NOT NULL CONSTRAINT [DF_RecurringCalls_CreationMode] DEFAULT 0");
			AddColumn("ManualAlertMinutes", "int NOT NULL CONSTRAINT [DF_RecurringCalls_ManualAlertMinutes] DEFAULT 15");
			AddColumn("UseRunCard", "bit NOT NULL CONSTRAINT [DF_RecurringCalls_UseRunCard] DEFAULT 0");
			AddColumn("AlertUserIds", "nvarchar(max) NULL");
			AddColumn("AlertGroupIds", "nvarchar(max) NULL");
			AddColumn("AlertRoleIds", "nvarchar(max) NULL");
			AddColumn("PausedOn", "datetime2 NULL");
			AddColumn("PausedByUserId", "nvarchar(128) NULL");
			AddColumn("PauseReason", "nvarchar(1000) NULL");
			AddColumn("ResumeOn", "datetime2 NULL");

			if (!Schema.Table("RecurringCallAlerts").Exists())
			{
				Create.Table("RecurringCallAlerts")
					.WithColumn("RecurringCallAlertId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecurringCallId").AsInt32().NotNullable()
					.WithColumn("OccurrenceOn").AsDateTime2().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("NotifiedOn").AsDateTime2().Nullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("AcknowledgedByUserId").AsString(128).Nullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId").AsString(128).Nullable()
					.WithColumn("Resolution").AsInt32().Nullable()
					.WithColumn("ResolutionNote").AsString(1000).Nullable();

				Create.Index("UX_RecurringCallAlerts_Occurrence").OnTable("RecurringCallAlerts")
					.OnColumn("RecurringCallId").Ascending()
					.OnColumn("OccurrenceOn").Ascending()
					.OnColumn("Kind").Ascending()
					.WithOptions().Unique();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecurringCallAlerts_Open' AND object_id = OBJECT_ID('RecurringCallAlerts')) " +
						"CREATE INDEX [IX_RecurringCallAlerts_Open] ON [RecurringCallAlerts] ([DepartmentId], [CreatedOn]) WHERE [ResolvedOn] IS NULL;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecurringCallAlerts_Department' AND object_id = OBJECT_ID('RecurringCallAlerts')) " +
						"CREATE INDEX [IX_RecurringCallAlerts_Department] ON [RecurringCallAlerts] ([DepartmentId], [ResolvedOn]);");
		}

		public override void Down()
		{
			if (Schema.Table("RecurringCallAlerts").Exists())
				Delete.Table("RecurringCallAlerts");

			foreach (var constraint in new[] { "DF_RecurringCalls_CreationMode", "DF_RecurringCalls_ManualAlertMinutes", "DF_RecurringCalls_UseRunCard" })
				Execute.Sql($"IF OBJECT_ID('{constraint}', 'D') IS NOT NULL ALTER TABLE [RecurringCalls] DROP CONSTRAINT [{constraint}];");

			foreach (var column in new[] { "Description", "ScheduleContactName", "ScheduleContactNumber", "ScheduleContactEmail", "FacilityName",
						 "FacilityDetails", "FacilityPoiId", "DestinationName", "DestinationDetails", "CreationMode", "ManualAlertMinutes", "UseRunCard",
						 "AlertUserIds", "AlertGroupIds", "AlertRoleIds", "PausedOn", "PausedByUserId", "PauseReason", "ResumeOn" })
				Execute.Sql($"IF COL_LENGTH('RecurringCalls', '{column}') IS NOT NULL ALTER TABLE [RecurringCalls] DROP COLUMN [{column}];");
		}

		private void AddColumn(string column, string definition)
		{
			Execute.Sql($"IF COL_LENGTH('RecurringCalls', '{column}') IS NULL ALTER TABLE [RecurringCalls] ADD [{column}] {definition};");
		}
	}
}
