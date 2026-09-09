using FluentMigrator;
namespace Resgrid.Providers.MigrationsPg.Migrations
{
    [Migration(204)]
    public class M0204_AddWorkOrderRecurrencesPg : Migration
    {
        private static string N(string value) => value.ToLowerInvariant();
        public override void Up()
        {
            Create.Table(N("WorkOrderRecurrences")).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("RequestId")).AsString(36).NotNullable()
                .WithColumn(N("CurrentVersionId")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("TargetUnitId")).AsInt32().Nullable()
                .WithColumn(N("TargetGroupId")).AsInt32().Nullable()
                .WithColumn(N("InventoryAssetId")).AsString(36).Nullable()
                .WithColumn(N("AssignedToUserId")).AsString(128).Nullable()
                .WithColumn(N("AssignedToRoleId")).AsInt32().Nullable()
                .WithColumn(N("Priority")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("IsActive")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("Calendar")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("Interval")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("TimeZoneId")).AsString(100).Nullable()
                .WithColumn(N("AnchorLocal")).AsDateTime().NotNullable()
                .WithColumn(N("EndOn")).AsDateTime().Nullable()
                .WithColumn(N("LeadDays")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("CompletionBased")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("ServiceWeekdays")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("ServiceStartMinute")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("ServiceEndMinute")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("BlackoutFrom")).AsDateTime().Nullable()
                .WithColumn(N("BlackoutUntil")).AsDateTime().Nullable()
                .WithColumn(N("NextDueOn")).AsDateTime().Nullable()
                .WithColumn(N("Cycle")).AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn(N("PendingWorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("ReadingDue")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("ReadingDueOn")).AsDateTime2().Nullable()
                .WithColumn(N("MeterUnit")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("MeterEpoch")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("MeterInterval")).AsDecimal(24, 6).Nullable()
                .WithColumn(N("MeterBaseline")).AsDecimal(24, 6).Nullable()
                .WithColumn(N("LastMeterValue")).AsDecimal(24, 6).Nullable()
                .WithColumn(N("LastReadingOn")).AsDateTime().Nullable()
                .WithColumn(N("Condition")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("ConditionThreshold")).AsDecimal(24, 6).Nullable()
                .WithColumn(N("ConditionLatched")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("EscalateAfterMinutes")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("EscalationRoleId")).AsInt32().Nullable();
            Create.ForeignKey(N("FK_WorkOrderRecurrences_Department")).FromTable(N("WorkOrderRecurrences")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderRecurrences", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderRecurrences", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderRecurrences", "Order", "WorkOrderId", "WorkOrders");
            Create.Table(N("WorkOrderRecurrenceVersions")).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("RecurrenceId")).AsInt32().NotNullable().WithDefaultValue(0);
            Create.ForeignKey(N("FK_WorkOrderRecurrenceVersions_Department")).FromTable(N("WorkOrderRecurrenceVersions")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderRecurrenceVersions", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderRecurrenceVersions", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderRecurrenceVersions", "Order", "WorkOrderId", "WorkOrders");
            Foreign("WorkOrderRecurrenceVersions", "Recurrence", "RecurrenceId", "WorkOrderRecurrences");
            Index("WorkOrderRecurrenceVersions", "Schedule", false, "DepartmentId", "RecurrenceId", "Id");
            Create.Table(N("WorkOrderMeterReadings")).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("RecurrenceId")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("RequestId")).AsString(36).Nullable()
                .WithColumn(N("MeterEpoch")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("ObservedOn")).AsDateTime().NotNullable();
            Create.ForeignKey(N("FK_WorkOrderMeterReadings_Department")).FromTable(N("WorkOrderMeterReadings")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderMeterReadings", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderMeterReadings", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderMeterReadings", "Order", "WorkOrderId", "WorkOrders");
            Foreign("WorkOrderMeterReadings", "Recurrence", "RecurrenceId", "WorkOrderRecurrences");
            Index("WorkOrderMeterReadings", "Schedule", false, "DepartmentId", "RecurrenceId", "Id");
            Create.Table(N("WorkOrderRecurrenceChanges")).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("RecurrenceId")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("ChangeType")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("OriginalDueOn")).AsDateTime().Nullable()
                .WithColumn(N("RevisedDueOn")).AsDateTime().Nullable();
            Create.ForeignKey(N("FK_WorkOrderRecurrenceChanges_Department")).FromTable(N("WorkOrderRecurrenceChanges")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderRecurrenceChanges", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderRecurrenceChanges", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderRecurrenceChanges", "Order", "WorkOrderId", "WorkOrders");
            Foreign("WorkOrderRecurrenceChanges", "Recurrence", "RecurrenceId", "WorkOrderRecurrences");
            Index("WorkOrderRecurrenceChanges", "Schedule", false, "DepartmentId", "RecurrenceId", "Id");
            Index("WorkOrderRecurrences", "Request", true, "DepartmentId", "RequestId");
            Index("WorkOrderRecurrences", "Due", false, "DepartmentId", "IsActive", "NextDueOn");
            Index("WorkOrderMeterReadings", "Request", true, "DepartmentId", "RequestId");
            foreach (var column in new[] { "RecurrenceVersionId", "EscalationRoleId" }) Alter.Table(N("WorkOrders")).AddColumn(N(column)).AsInt32().Nullable();
            Alter.Table(N("WorkOrders")).AddColumn(N("RecurrenceCycle")).AsInt64().Nullable();
            Alter.Table(N("WorkOrders")).AddColumn(N("EscalateAfterMinutes")).AsInt32().NotNullable().WithDefaultValue(0);
            foreach (var column in new[] { "OriginalDueOn", "EscalatedOn" }) Alter.Table(N("WorkOrders")).AddColumn(N(column)).AsDateTime().Nullable();
            Foreign("WorkOrders", "RecurrenceVersion", "RecurrenceVersionId", "WorkOrderRecurrenceVersions");
            Execute.Sql("CREATE UNIQUE INDEX ux_workorders_cycle ON workorders(departmentid,workorderrecurrenceid,recurrencecycle) WHERE recurrencecycle IS NOT NULL;");
            Index("WorkOrders", "Overdue", false, "DepartmentId", "EscalatedOn", "DueOn");
        }
        private void Foreign(string table, string suffix, string column, string parent) => Create.ForeignKey(N("FK_" + table + "_" + suffix)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column)).ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
        private void Index(string table, string suffix, bool unique, params string[] columns)
        {
            var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
            for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
            if (unique) index.WithOptions().Unique();
        }
        public override void Down()
        {
            Execute.Sql("DO $$ BEGIN IF exists (select 1 from workorderrecurrences) or exists (select 1 from workorders where recurrenceversionid is not null or escalatedon is not null or originaldueon is not null) THEN RAISE EXCEPTION 'Readiness evidence requires authorized retention before rollback.'; END IF; END $$;");
            Delete.ForeignKey(N("FK_WorkOrders_RecurrenceVersion")).OnTable(N("WorkOrders"));
            Delete.Index(N("UX_WorkOrders_Cycle")).OnTable(N("WorkOrders"));
            Delete.Index(N("IX_WorkOrders_Overdue")).OnTable(N("WorkOrders"));
            foreach (var column in new[] { "RecurrenceVersionId", "RecurrenceCycle", "OriginalDueOn", "EscalatedOn", "EscalateAfterMinutes", "EscalationRoleId" }) Delete.Column(N(column)).FromTable(N("WorkOrders"));
            foreach (var table in new[] { "WorkOrderRecurrenceChanges", "WorkOrderMeterReadings", "WorkOrderRecurrenceVersions", "WorkOrderRecurrences" }) Delete.Table(N(table));
        }
    }
}
