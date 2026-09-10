using FluentMigrator;
namespace Resgrid.Providers.Migrations.Migrations
{
    [Migration(203)]
    public class M0203_AddWorkOrderIntegrations : Migration
    {
        private static string N(string value) => value;
        public override void Up()
        {
            Create.Table(N("WorkOrderFailureIntents"))
                .WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("CompletionId")).AsString(36).Nullable()
                .WithColumn(N("ItemId")).AsString(36).Nullable()
                .WithColumn(N("VersionId")).AsString(36).Nullable()
                .WithColumn(N("OccurrenceId")).AsString(36).Nullable()
                .WithColumn(N("AuthorizedBy")).AsString(128).Nullable()
                .WithColumn(N("TargetType")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("TargetId")).AsString(36).Nullable()
                .WithColumn(N("TargetGroupId")).AsInt32().Nullable()
                .WithColumn(N("Priority")).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(N("HoldUnit")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("HoldAsset")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("ProcessedOn")).AsDateTime2().Nullable();
            Create.ForeignKey(N("FK_WorkOrderFailureIntents_Department")).FromTable(N("WorkOrderFailureIntents")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderFailureIntents", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderFailureIntents", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderFailureIntents", "Order", "WorkOrderId", "WorkOrders");
            Create.Table(N("WorkOrderSafetyHolds"))
                .WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn(N("UnitId")).AsInt32().Nullable()
                .WithColumn(N("AssetId")).AsString(36).Nullable()
                .WithColumn(N("PreviousState")).AsInt32().Nullable()
                .WithColumn(N("AppliedStateId")).AsInt32().Nullable()
                .WithColumn(N("AppliedAssetRevision")).AsInt32().Nullable()
                .WithColumn(N("ReleasedOn")).AsDateTime2().Nullable()
                .WithColumn(N("ReleasedBy")).AsString(128).Nullable()
                .WithColumn(N("StateRestored")).AsBoolean().NotNullable().WithDefaultValue(false);
            Create.ForeignKey(N("FK_WorkOrderSafetyHolds_Department")).FromTable(N("WorkOrderSafetyHolds")).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index("WorkOrderSafetyHolds", "Tenant", true, "DepartmentId", "Id");
            Index("WorkOrderSafetyHolds", "Order", false, "DepartmentId", "WorkOrderId", "Id");
            Foreign("WorkOrderSafetyHolds", "Order", "WorkOrderId", "WorkOrders");
            Index("WorkOrderFailureIntents", "Failure", true, "DepartmentId", "CompletionId", "ItemId");
            Index("WorkOrderFailureIntents", "Pending", false, "DepartmentId", "ProcessedOn", "Id");
            Index("WorkOrderSafetyHolds", "Unit", false, "DepartmentId", "UnitId", "ReleasedOn");
            Index("WorkOrderSafetyHolds", "Asset", false, "DepartmentId", "AssetId", "ReleasedOn");
            foreach (var column in new[] { "InventoryOperationId", "InventoryReversalId", "InventoryRequestId" })
                Alter.Table(N("WorkOrderParts")).AddColumn(N(column)).AsString(36).Nullable();
            Execute.Sql("CREATE UNIQUE INDEX UX_WorkOrderParts_InventoryRequest ON WorkOrderParts(DepartmentId,InventoryRequestId) WHERE InventoryRequestId IS NOT NULL;");
            Alter.Table(N("InventoryTransactions")).AddColumn(N("WorkOrderPartId")).AsInt32().Nullable();
            Foreign("InventoryTransactions", "WorkOrderPart", "WorkOrderPartId", "WorkOrderParts");
            Index("InventoryTransactions", "WorkOrderPart", false, "DepartmentId", "WorkOrderPartId");
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
            Execute.Sql("IF EXISTS (SELECT 1 FROM WorkOrderFailureIntents) OR EXISTS (SELECT 1 FROM WorkOrderSafetyHolds) OR EXISTS (SELECT 1 FROM InventoryTransactions WHERE WorkOrderPartId IS NOT NULL) OR EXISTS (SELECT 1 FROM WorkOrderParts WHERE InventoryOperationId IS NOT NULL OR InventoryRequestId IS NOT NULL) THROW 51000, 'Readiness evidence requires authorized retention before rollback.', 1;");
            Delete.ForeignKey(N("FK_InventoryTransactions_WorkOrderPart")).OnTable(N("InventoryTransactions"));
            Delete.Index(N("IX_InventoryTransactions_WorkOrderPart")).OnTable(N("InventoryTransactions"));
            Delete.Index(N("UX_WorkOrderParts_InventoryRequest")).OnTable(N("WorkOrderParts"));
            Delete.Column(N("WorkOrderPartId")).FromTable(N("InventoryTransactions"));
            foreach (var column in new[] { "InventoryOperationId", "InventoryReversalId", "InventoryRequestId" }) Delete.Column(N(column)).FromTable(N("WorkOrderParts"));
            Delete.Table(N("WorkOrderSafetyHolds"));
            Delete.Table(N("WorkOrderFailureIntents"));
        }
    }
}
