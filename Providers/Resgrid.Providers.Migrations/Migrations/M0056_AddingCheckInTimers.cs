using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(56)]
	public class M0056_AddingCheckInTimers : Migration
	{
		public override void Up()
		{
			// ── CheckInTimerConfigs ─────────────────────────────────
			Create.Table("CheckInTimerConfigs")
				.WithColumn("CheckInTimerConfigId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("TimerTargetType").AsInt32().NotNullable()
				.WithColumn("UnitTypeId").AsInt32().Nullable()
				.WithColumn("DurationMinutes").AsInt32().NotNullable()
				.WithColumn("WarningThresholdMinutes").AsInt32().NotNullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Create.ForeignKey("FK_CheckInTimerConfigs_Departments")
				.FromTable("CheckInTimerConfigs").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_CheckInTimerConfigs_DepartmentId")
				.OnTable("CheckInTimerConfigs")
				.OnColumn("DepartmentId");

			Create.UniqueConstraint("UQ_CheckInTimerConfigs_Dept_Target_Unit")
				.OnTable("CheckInTimerConfigs")
				.Columns("DepartmentId", "TimerTargetType", "UnitTypeId");

			// ── CheckInTimerOverrides ──────────────────────────────
			Create.Table("CheckInTimerOverrides")
				.WithColumn("CheckInTimerOverrideId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("CallTypeId").AsInt32().Nullable()
				.WithColumn("CallPriority").AsInt32().Nullable()
				.WithColumn("TimerTargetType").AsInt32().NotNullable()
				.WithColumn("UnitTypeId").AsInt32().Nullable()
				.WithColumn("DurationMinutes").AsInt32().NotNullable()
				.WithColumn("WarningThresholdMinutes").AsInt32().NotNullable()
				.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
				.WithColumn("CreatedByUserId").AsString(128).NotNullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable();

			Create.ForeignKey("FK_CheckInTimerOverrides_Departments")
				.FromTable("CheckInTimerOverrides").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_CheckInTimerOverrides_DepartmentId")
				.OnTable("CheckInTimerOverrides")
				.OnColumn("DepartmentId");

			Create.UniqueConstraint("UQ_CheckInTimerOverrides_Dept_Call_Target_Unit")
				.OnTable("CheckInTimerOverrides")
				.Columns("DepartmentId", "CallTypeId", "CallPriority", "TimerTargetType", "UnitTypeId");

			// ── CheckInRecords ─────────────────────────────────────
			Create.Table("CheckInRecords")
				.WithColumn("CheckInRecordId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("CallId").AsInt32().NotNullable()
				.WithColumn("CheckInType").AsInt32().NotNullable()
				.WithColumn("UserId").AsString(128).NotNullable()
				.WithColumn("UnitId").AsInt32().Nullable()
				.WithColumn("Latitude").AsString(50).Nullable()
				.WithColumn("Longitude").AsString(50).Nullable()
				.WithColumn("Timestamp").AsDateTime2().NotNullable()
				.WithColumn("Note").AsString(1000).Nullable();

			Create.ForeignKey("FK_CheckInRecords_Departments")
				.FromTable("CheckInRecords").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_CheckInRecords_Calls")
				.FromTable("CheckInRecords").ForeignColumn("CallId")
				.ToTable("Calls").PrimaryColumn("CallId");

			Create.Index("IX_CheckInRecords_CallId")
				.OnTable("CheckInRecords")
				.OnColumn("CallId");

			Create.Index("IX_CheckInRecords_DepartmentId_Timestamp")
				.OnTable("CheckInRecords")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("Timestamp").Descending();

			// ── Alter Calls ────────────────────────────────────────
			Alter.Table("Calls")
				.AddColumn("CheckInTimersEnabled").AsBoolean().NotNullable().WithDefaultValue(false);

			// ── Alter CallQuickTemplates ────────────────────────────
			Alter.Table("CallQuickTemplates")
				.AddColumn("CheckInTimersEnabled").AsBoolean().Nullable();
		}

		public override void Down()
		{
			Delete.Column("CheckInTimersEnabled").FromTable("CallQuickTemplates");
			Delete.Column("CheckInTimersEnabled").FromTable("Calls");

			Delete.ForeignKey("FK_CheckInRecords_Calls").OnTable("CheckInRecords");
			Delete.ForeignKey("FK_CheckInRecords_Departments").OnTable("CheckInRecords");
			Delete.Table("CheckInRecords");

			Delete.ForeignKey("FK_CheckInTimerOverrides_Departments").OnTable("CheckInTimerOverrides");
			Delete.Table("CheckInTimerOverrides");

			Delete.ForeignKey("FK_CheckInTimerConfigs_Departments").OnTable("CheckInTimerConfigs");
			Delete.Table("CheckInTimerConfigs");
		}
	}
}
