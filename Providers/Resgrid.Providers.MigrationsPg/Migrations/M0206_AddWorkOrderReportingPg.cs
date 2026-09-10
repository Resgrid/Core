using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
    [Migration(206)]
    public class M0206_AddWorkOrderReportingPg : Migration
    {
        private static string N(string value) => value.ToLowerInvariant();
        public override void Up()
        {
            Create.Table(N("WorkOrderReportSnapshots"))
                .WithColumn(N("Id")).AsInt64().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().NotNullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable()
                .WithColumn(N("RecordedOn")).AsDateTime().NotNullable()
                .WithColumn(N("SourceActivityId")).AsInt32().Nullable()
                .WithColumn(N("RecurrenceVersionId")).AsInt32().Nullable()
                .WithColumn(N("SourceType")).AsInt32().NotNullable()
                .WithColumn(N("Status")).AsInt32().NotNullable()
                .WithColumn(N("Priority")).AsInt32().NotNullable()
                .WithColumn(N("TargetUnitId")).AsInt32().Nullable()
                .WithColumn(N("TargetGroupId")).AsInt32().Nullable()
                .WithColumn(N("InventoryAssetId")).AsString(36).Nullable()
                .WithColumn(N("DueOn")).AsDateTime().Nullable()
                .WithColumn(N("StartedOn")).AsDateTime().Nullable()
                .WithColumn(N("CompletedOn")).AsDateTime().Nullable()
                .WithColumn(N("ClosedOn")).AsDateTime().Nullable();
            Foreign("Order", "WorkOrderId", "WorkOrders");
            Foreign("Activity", "SourceActivityId", "WorkOrderActivities");
            Foreign("Template", "RecurrenceVersionId", "WorkOrderRecurrenceVersions");
            Index("WorkOrderReportSnapshots", "Revision", true, "DepartmentId", "WorkOrderId", "Revision");
            Index("WorkOrderReportSnapshots", "AsOf", false, "DepartmentId", "WorkOrderId", "RecordedOn", "Id");
            Index("WorkOrders", "ReportCreated", false, "DepartmentId", "CreatedOn", "Id");
            // PostgreSQL checks this self-reference for every deleted parent; a missing child index
            // turns an authorized large-department purge into repeated full scans.
            Index("WorkOrders", "DuplicateParent", false, "DepartmentId", "DuplicateOfId");
            Index("WorkOrderReportSnapshots", "Unit", false, "DepartmentId", "TargetUnitId", "RecordedOn", "WorkOrderId");
            Index("WorkOrderReportSnapshots", "Asset", false, "DepartmentId", "InventoryAssetId", "RecordedOn", "WorkOrderId");
        }
        private void Foreign(string suffix, string column, string parent) => Create.ForeignKey(N("FK_WorkOrderReportSnapshots_" + suffix))
            .FromTable(N("WorkOrderReportSnapshots")).ForeignColumns(N("DepartmentId"), N(column))
            .ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
        private void Index(string table, string suffix, bool unique, params string[] columns)
        {
            var index = Create.Index(N((unique ? "UX_" : "IX_") + table + "_" + suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
            for (var i = 1; i < columns.Length; i++) index = index.OnColumn(N(columns[i])).Ascending();
            if (unique) index.WithOptions().Unique();
        }
        public override void Down()
        {
            Execute.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM workorderreportsnapshots) THEN RAISE EXCEPTION 'Readiness evidence requires authorized retention before rollback.'; END IF; END $$;");
            Delete.Table(N("WorkOrderReportSnapshots"));
            Delete.Index(N("IX_WorkOrders_ReportCreated")).OnTable(N("WorkOrders"));
            Delete.Index(N("IX_WorkOrders_DuplicateParent")).OnTable(N("WorkOrders"));
        }
    }
}
