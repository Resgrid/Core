using System;
using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
    [Migration(207)]
    public sealed class M0207_AddWorkOrderOperationsPg : Migration
    {
        private string N(string value) => value.ToLowerInvariant();
        public override void Up()
        {
            Base("WorkOrderPolicies");
            Alter.Table(N("WorkOrderPolicies")).AddColumn(N("CalendarJson")).AsString(int.MaxValue).Nullable();
            Index("WorkOrderPolicies", "Department", true, "DepartmentId");
            Base("WorkOrderOperationReceipts");
            Alter.Table(N("WorkOrderOperationReceipts")).AddColumn(N("RequestId")).AsString(36).NotNullable()
                .AddColumn(N("Kind")).AsInt32().NotNullable().WithDefaultValue(0);
            Index("WorkOrderOperationReceipts", "Request", true, "DepartmentId", "RequestId");
            Base("WorkOrderVendorCharges");
            Alter.Table(N("WorkOrderVendorCharges")).AddColumn(N("RequestId")).AsString(36).NotNullable()
                .AddColumn(N("VoidedOn")).AsDateTime2().Nullable();
            Index("WorkOrderVendorCharges", "Request", true, "DepartmentId", "RequestId");
            Base("WorkOrderPartMovements");
            Alter.Table(N("WorkOrderPartMovements")).AddColumn(N("PartId")).AsInt32().NotNullable()
                .AddColumn(N("RequestId")).AsString(36).NotNullable()
                .AddColumn(N("Kind")).AsInt32().NotNullable()
                .AddColumn(N("Quantity")).AsDecimal(24,6).NotNullable()
                .AddColumn(N("FromLocationId")).AsString(36).Nullable()
                .AddColumn(N("ToLocationId")).AsString(36).Nullable()
                .AddColumn(N("InventoryOperationId")).AsString(36).Nullable()
                .AddColumn(N("InventoryTransactionId")).AsString(36).Nullable()
                .AddColumn(N("Cancelled")).AsBoolean().NotNullable().WithDefaultValue(false);
            Foreign("WorkOrderPartMovements", "Part", "PartId", "WorkOrderParts");
            Index("WorkOrderPartMovements", "Request", true, "DepartmentId", "RequestId");
            Index("WorkOrderPartMovements", "Part", false, "DepartmentId", "PartId", "Id");
            foreach (var name in new[] { "ResponseDueOn", "RepairDueOn", "ResponseOn", "ResponseBreachedOn", "RepairBreachedOn" })
                Alter.Table(N("WorkOrders")).AddColumn(N(name)).AsDateTime2().Nullable();
            Alter.Table(N("WorkOrders")).AddColumn(N("SlaPolicyRevision")).AsInt32().Nullable();
            foreach (var name in new[] { "ResponseDueOn", "RepairDueOn", "ResponseOn", "ResponseBreachedOn", "RepairBreachedOn" })
                Alter.Table(N("WorkOrderReportSnapshots")).AddColumn(N(name)).AsDateTime2().Nullable();
            Alter.Table(N("WorkOrderReportSnapshots")).AddColumn(N("SlaPolicyRevision")).AsInt32().Nullable();
            Alter.Table(N("WorkOrderParts")).AddColumn(N("Staged")).AsBoolean().NotNullable().WithDefaultValue(false);
            foreach (var name in new[] { "ReservedLocationId", "IssuedLocationId", "ReservedAssetId", "ReservedLotId" })
                Alter.Table(N("WorkOrderParts")).AddColumn(N(name)).AsString(36).Nullable();
            foreach (var name in new[] { "ReservedQuantity", "IssuedQuantity", "ConsumedQuantity", "ReturnedQuantity" })
                Alter.Table(N("WorkOrderParts")).AddColumn(N(name)).AsDecimal(24,6).NotNullable().WithDefaultValue(0);
            Index("WorkOrderParts", "Reservations", false, "DepartmentId", "InventoryItemId", "ReservedLocationId", "ReservedLotId");
            Index("WorkOrders", "ResponseSla", false, "DepartmentId", "ResponseDueOn", "ResponseBreachedOn");
            Index("WorkOrders", "RepairSla", false, "DepartmentId", "RepairDueOn", "RepairBreachedOn");
            if (Schema.Table(N("InventoryTransactions")).Exists())
            {
                Alter.Table(N("InventoryTransactions")).AddColumn(N("WorkOrderPartMovementId")).AsInt32().Nullable();
                Foreign("InventoryTransactions", "WorkOrderMovement", "WorkOrderPartMovementId", "WorkOrderPartMovements");
                Index("InventoryTransactions", "WorkOrderMovement", false, "DepartmentId", "WorkOrderPartMovementId");
            }
        }
        private void Base(string table)
        {
            Create.Table(N(table)).WithColumn(N("Id")).AsInt32().PrimaryKey().Identity()
                .WithColumn(N("DepartmentId")).AsInt32().NotNullable()
                .WithColumn(N("WorkOrderId")).AsInt32().Nullable()
                .WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
                .WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
                .WithColumn(N("CreatedBy")).AsString(128).NotNullable()
                .WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false);
            Create.ForeignKey(N("FK_"+table+"_Department")).FromTable(N(table)).ForeignColumn(N("DepartmentId")).ToTable(N("Departments")).PrimaryColumn(N("DepartmentId"));
            Index(table, "Tenant", true, "DepartmentId", "Id");
            Foreign(table, "Order", "WorkOrderId", "WorkOrders");
            Index(table, "Order", false, "DepartmentId", "WorkOrderId", "Id");
        }
        private void Foreign(string table, string suffix, string column, string parent) => Create.ForeignKey(N("FK_"+table+"_"+suffix)).FromTable(N(table)).ForeignColumns(N("DepartmentId"), N(column)).ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
        private void Index(string table, string suffix, bool unique, params string[] columns)
        {
            var index=Create.Index(N((unique?"UX_":"IX_")+table+"_"+suffix)).OnTable(N(table)).OnColumn(N(columns[0])).Ascending();
            for(var i=1;i<columns.Length;i++) index=index.OnColumn(N(columns[i])).Ascending();
            if(unique) index.WithOptions().Unique();
        }
        public override void Down()
        {
            Execute.Sql("DO $$ BEGIN IF EXISTS(SELECT 1 FROM workorderpolicies) OR EXISTS(SELECT 1 FROM workorderoperationreceipts) OR EXISTS(SELECT 1 FROM workordervendorcharges) OR EXISTS(SELECT 1 FROM workorderpartmovements) OR EXISTS(SELECT 1 FROM workorderreportsnapshots WHERE slapolicyrevision IS NOT NULL) OR EXISTS(SELECT 1 FROM workorders WHERE slapolicyrevision IS NOT NULL) OR EXISTS(SELECT 1 FROM workorderparts WHERE staged=true) THEN RAISE EXCEPTION 'Retained work-order operations prevent rollback'; END IF; END $$;");
            if(Schema.Table(N("InventoryTransactions")).Exists())
            {
                Delete.ForeignKey(N("FK_InventoryTransactions_WorkOrderMovement")).OnTable(N("InventoryTransactions"));
                Delete.Index(N("IX_InventoryTransactions_WorkOrderMovement")).OnTable(N("InventoryTransactions"));
                Delete.Column(N("WorkOrderPartMovementId")).FromTable(N("InventoryTransactions"));
            }
            foreach(var suffix in new[] {"ResponseSla","RepairSla"}) Delete.Index(N("IX_WorkOrders_"+suffix)).OnTable(N("WorkOrders"));
            foreach(var name in new[] {"ResponseDueOn","RepairDueOn","ResponseOn","ResponseBreachedOn","RepairBreachedOn","SlaPolicyRevision"}) Delete.Column(N(name)).FromTable(N("WorkOrders"));
            foreach(var name in new[] {"ResponseDueOn","RepairDueOn","ResponseOn","ResponseBreachedOn","RepairBreachedOn","SlaPolicyRevision"}) Delete.Column(N(name)).FromTable(N("WorkOrderReportSnapshots"));
            Delete.Index(N("IX_WorkOrderParts_Reservations")).OnTable(N("WorkOrderParts"));
            foreach(var name in new[] {"Staged","ReservedLocationId","IssuedLocationId","ReservedAssetId","ReservedLotId","ReservedQuantity","IssuedQuantity","ConsumedQuantity","ReturnedQuantity"}) Delete.Column(N(name)).FromTable(N("WorkOrderParts"));
            foreach(var table in new[] {"WorkOrderPartMovements","WorkOrderVendorCharges","WorkOrderOperationReceipts","WorkOrderPolicies"}) Delete.Table(N(table));
        }
    }
}

